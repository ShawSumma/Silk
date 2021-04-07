﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Humanizer;
using Humanizer.Localisation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Silk.Core.Data.MediatR.Unified.Reminders;
using Silk.Core.Data.Models;
using Silk.Extensions;

namespace Silk.Core.Discord.Services
{
    public class ReminderService : BackgroundService
    {
        private const string MissingChannel = "Hey!, you wanted me to remind you of something, but the channel was deleted, or is otherwise inaccessible to me now.\n";

        private List<Reminder> _reminders; // We're gonna slurp all reminders into memory. Yolo, I guess.

        private readonly IServiceProvider _services;
        private readonly ILogger<ReminderService> _logger;
        private readonly DiscordShardedClient _client;

        public ReminderService(ILogger<ReminderService> logger, IServiceProvider services, DiscordShardedClient client)
        {
            _logger = logger;
            _services = services;
            _client = client;
        }

        public async Task CreateReminder
        (
            DateTime expiration, ulong ownerId,
            ulong channelId, ulong messageId, ulong guildId,
            string messageContent, bool wasReply,
            ReminderType type = ReminderType.Once, ulong? replyId = null,
            ulong? replyAuthorId = null, string? replyMessageContent = null)
        {
            using IServiceScope scope = _services.CreateScope();
            var mediator = scope.ServiceProvider.Get<IMediator>();
            Reminder reminder = await mediator!.Send(new CreateReminderRequest(expiration, ownerId, channelId, messageId, guildId, messageContent, wasReply, type, replyId, replyAuthorId, replyMessageContent));
            _reminders.Add(reminder);
        }

        public async Task<IEnumerable<Reminder>?> GetRemindersAsync(ulong userId)
        {
            IEnumerable<Reminder> reminders = _reminders.Where(r => r.OwnerId == userId);
            if (reminders.Count() is 0) return null;
            return reminders;
        }

        public async Task RemoveReminderAsync(int id)
        {
            Reminder? reminder = _reminders.SingleOrDefault(r => r.Id == id);
            if (reminder is not null)
            {
                using IServiceScope scope = _services.CreateScope();
                var mediator = _services.CreateScope().ServiceProvider.Get<IMediator>();
                _reminders.Remove(reminder);
                await mediator!.Send(new RemoveReminderRequest(id));
            }
        }

        private async Task Tick()
        {
            // ReSharper disable once ForCanBeConvertedToForeach //
            //             Collection gets modified.             //
            for (int i = 0; i < _reminders.Count; i++)
            {
                Reminder r = _reminders[i];
                if (r.Expiration < DateTime.UtcNow)
                    await DispatchReminderAsync(r);
            }
        }

        private async Task DispatchReminderAsync(Reminder reminder)
        {
            var guilds = _client.ShardClients.SelectMany(s => s.Value.Guilds);
            if (!(guilds.FirstOrDefault(g => g.Key == reminder.GuildId).Value is { } guild))
            {
                _logger.LogWarning("Couldn't find guild {GuildId}! Removing reminders from queue", reminder.GuildId);
                _reminders.RemoveAll(r => r.GuildId == reminder.GuildId);
            }
            else
            {
                if (reminder.Type is ReminderType.Once)
                {
                    _logger.LogTrace("Dequeing reminder");
                    _reminders.Remove(reminder);
                }

                if (!guild.Channels.TryGetValue(reminder.ChannelId, out var channel)) { await SendDmReminderAsync(reminder, guild); }
                else { await SendGuildReminderAsync(reminder, channel); }

                using IServiceScope scope = _services.CreateScope();
                var mediator = scope.ServiceProvider.Get<IMediator>();

                if (reminder.Type is ReminderType.Once)
                {
                    await RemoveReminderAsync(reminder.Id);
                    return;
                }
                DateTime time = reminder.Type switch
                {
                    ReminderType.Hourly => DateTime.UtcNow + TimeSpan.FromHours(1),
                    ReminderType.Daily => DateTime.UtcNow + TimeSpan.FromDays(1),
                    ReminderType.Weekly => DateTime.UtcNow + TimeSpan.FromDays(7),
                    ReminderType.Monthly => DateTime.UtcNow + TimeSpan.FromDays(30)
                };
                int index = _reminders.IndexOf(reminder);
                _reminders[index] = await mediator!.Send(new UpdateReminderRequest(reminder, time));
            }
        }

        private async Task SendGuildReminderAsync(Reminder reminder, DiscordChannel channel)
        {
            _logger.LogTrace("Preparing to send reminder");
            var builder = new DiscordMessageBuilder().WithAllowedMention(new UserMention(reminder.OwnerId));
            var mention = reminder.WasReply ? $" <@{reminder.OwnerId}>," : null;
            var message = $"Hey{mention}! {(DateTime.UtcNow - reminder.CreationTime).Humanize(2, minUnit: TimeUnit.Second)} ago:\n{reminder.MessageContent}";

            // These are misleading names (They don't actually dispatch a message) I know but w/e. //
            if (reminder.WasReply) { await SendReplyReminderAsync(reminder, channel, builder, message); }
            else { await SendReminderAsync(reminder, channel, builder, message); }

            await builder.SendAsync(channel);
            _logger.LogTrace("Sent reminder succesfully");
        }

        private static async Task SendReminderAsync(Reminder reminder, DiscordChannel channel, DiscordMessageBuilder builder, string message)
        {
            bool validMessage;

            try { validMessage = await channel.GetMessageAsync(reminder.MessageId) is not null; }
            catch (NotFoundException) { validMessage = false; }
            if (validMessage)
            {
                builder.WithReply(reminder.MessageId, true);
                builder.WithContent($"You wanted me to remind you of this {(DateTime.UtcNow - reminder.CreationTime).Humanize(2, minUnit: TimeUnit.Second)} ago!");
            }
            else
            {
                message += "\n(Your message was deleted, hence the lack of a reply!)";
                builder.WithContent(message);
            }
        }
        private static async Task SendReplyReminderAsync(Reminder reminder, DiscordChannel channel, DiscordMessageBuilder builder, string message)
        {
            bool validReply;

            try { validReply = await channel.GetMessageAsync(reminder.ReplyId.Value) is not null; }
            catch (NotFoundException) { validReply = false; }

            if (validReply)
            {
                builder.WithReply(reminder.ReplyId.Value);
                builder.WithContent(message);
            }
            else
            {
                message += "\n(You replied to someone, but that message was deleted!)\n";
                message += $"Replying to:\n> {reminder.ReplyMessageContent!.Pull(..250)}\n" +
                           $"From: <@{reminder.ReplyAuthorId}>";
                builder.WithContent(message);
            }
        }

        private async Task SendDmReminderAsync(Reminder reminder, DiscordGuild guild)
        {
            _logger.LogTrace("Channel doesn't exist on guild! Attempting to DM user");

            try
            {
                DiscordMember member = await guild.GetMemberAsync(reminder.OwnerId);
                await member.SendMessageAsync(MissingChannel + $"{(DateTime.UtcNow - reminder.CreationTime).Humanize(2, minUnit: TimeUnit.Second)} ago: \n{reminder.MessageContent}");
            }
            catch (UnauthorizedException) { _logger.LogTrace("Failed to message user, skipping "); }
            catch (NotFoundException) { _logger.LogTrace("Member left guild, skipping"); }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Started!");

            using IServiceScope scope = _services.CreateScope();
            var mediator = scope.ServiceProvider.Get<IMediator>();

            _reminders = (await mediator!.Send(new GetAllRemindersRequest(), stoppingToken)).ToList();
            _logger.LogTrace("Slurped {ReminderCount} reminders into memory", _reminders.Count);
            _logger.LogDebug("Starting reminder callback timer");

            var timer = new Timer(__ => _ = Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

            try { await Task.Delay(-1, stoppingToken); }
            catch (TaskCanceledException) { }
            finally
            {
                _logger.LogDebug("Cancelation requested. Stopping service");
                await timer.DisposeAsync();
                // It's safe to clear the list as it's all saved to the database prior when they're added. //
                _reminders.Clear();
            }
        }
    }
}