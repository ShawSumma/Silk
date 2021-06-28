﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Humanizer;
using Silk.Core.Data.Models;
using Silk.Core.Services.Data;
using Silk.Core.Services.Interfaces;
using Silk.Core.Utilities;
using Silk.Core.Utilities.HelpFormatter;
using Silk.Extensions;

namespace Silk.Core.Commands.Moderation
{
    [Category(Categories.Mod)]
    public class MuteCommand : BaseCommandModule
    {
        private readonly ConfigService _configService;
        private readonly IModerationService _moderationService;
        private readonly IInfractionService _infractions;


        public MuteCommand(ConfigService configService, IModerationService moderationService, IInfractionService infractions)
        {
            _configService = configService;
            _moderationService = moderationService;
            _infractions = infractions;
        }

        [Command("mute")]
        [RequireFlag(UserFlag.Staff)]
        [RequirePermissions(Permissions.ManageRoles)]
        [Description("Mute a guild member!")]
        [Priority(0)]
        public async Task Mute(CommandContext ctx, DiscordMember user, [RemainingText] string reason = "Not Given.")
        {
            DiscordMember bot = ctx.Guild.CurrentMember;


            if (user.IsAbove(bot))
            {
                int roleDiff = user.Roles.Max(r => r.Position) - ctx.Member.Roles.Max(r => r.Position);
                string message;

                message = roleDiff is not 0 ?
                    $"I can't do that! They're {roleDiff} role(s) above me!" :
                    "We have the same top role! I can't add roles to this person.";

                await ctx.RespondAsync(message);
                return;
            }

            if (false && user.IsAbove(ctx.Member))
            {
                int roleDiff = user.Roles.Max(r => r.Position) - ctx.Member.Roles.Max(r => r.Position);
                string message;

                message = roleDiff is not 0 ?
                    $"I can't do that! They're {roleDiff} role(s) above you!" :
                    "You two see eye to eye! c: I can't mute someone with the same role as you.";

                await ctx.RespondAsync(message);
                return;
            }
            var sw = Stopwatch.StartNew();
            var res = await _infractions.MuteAsync(user.Id, ctx.Guild.Id, ctx.User.Id, reason, null);
            sw.Stop();
            await ctx.RespondAsync($"Mute result returned {res.Humanize(LetterCasing.Sentence)} in {sw.ElapsedMilliseconds} ms");
        }

        [Priority(1)]
        [Command("mute")]
        [RequireFlag(UserFlag.Staff)]
        public async Task TempMute(CommandContext ctx, DiscordMember user, TimeSpan duration, [RemainingText] string reason = "Not Given.")
        {
            DiscordMember bot = ctx.Guild.CurrentMember;
            if (user.IsAbove(bot))
            {
                int roleDiff = user.Roles.Max(r => r.Position) - ctx.Member.Roles.Max(r => r.Position);
                string message;

                message = roleDiff is not 0 ?
                    $"I can't do that! They're {roleDiff} role(s) above me!" :
                    "We have the same top role! I can't add roles to this person.";

                await ctx.RespondAsync(message);
                return;
            }

            if (user.IsAbove(ctx.Member))
            {
                int roleDiff = user.Roles.Max()!.Position - ctx.Member.Roles.Max()!.Position;
                string message;

                message = roleDiff is not 0 ?
                    $"I can't do that! They're {roleDiff} role(s) above you!" :
                    "You two see eye to eye! c: I can't mute someone with the same role as you.";

                await ctx.RespondAsync(message);
                return;
            }

            GuildConfig config = (await _configService.GetConfigAsync(ctx.Guild.Id))!;

            if (config.MuteRoleId is 0)
            {
                await ctx.RespondAsync("Mute role isn't configured on this server!"); // TODO: Generate mute role on-demand. 
                return;
            }

            Infraction infraction = await _moderationService.CreateTempInfractionAsync(user, ctx.Member,
                InfractionType.Mute, reason, DateTime.Now.Add(duration));

            await _moderationService.MuteAsync(user, ctx.Channel, infraction);
            await ctx.RespondAsync($":white_check_mark: Muted {user.Username}.");
        }
    }
}