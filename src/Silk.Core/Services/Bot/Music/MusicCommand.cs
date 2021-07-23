using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Silk.Core.Utilities.HttpClient;
using YoutubeExplode;
using YoutubeExplode.Videos;

namespace Silk.Core.Services.Bot.Music
{
	public class MusicCommand : BaseCommandModule
	{
		private readonly MusicVoiceService _music;
		private readonly YoutubeClient _ytClient;
		private readonly IHttpClientFactory _htClientFactory;
		
		public MusicCommand(MusicVoiceService music, YoutubeClient ytClient, IHttpClientFactory htClientFactory)
		{
			_music = music;
			_ytClient = ytClient;
			_htClientFactory = htClientFactory;
		}

		[Hidden]
		[Command]
		public async Task Join(CommandContext ctx)
		{
			if (ctx.Member.VoiceState?.Channel is null)
			{
				await ctx.RespondAsync("Join a voice channel!");
				return;
			}
			
			if (ctx.Member.VoiceState.Channel.Type is not (ChannelType.Voice or ChannelType.Stage))
			{
				await ctx.RespondAsync("Uh?? You appear to be in an invalid channel.");
				return;
			}

			if (await _music.JoinAsync(ctx.Member.VoiceState.Channel))
				await ctx.Message.CreateReactionAsync(DiscordEmoji.FromUnicode("👍🏽"));
			else
				await ctx.RespondAsync("I couldn't join that channel!"); //TODO: Return error code
		}

		[Hidden]
		[Command]
		public async Task Play(CommandContext ctx, VideoId video)
		{
			if (!_music.IsInChannel(ctx.Member))
			{
				await ctx.RespondAsync("Join a voice channel!");
				return;
			}

			if (!_music.IsInChannel(ctx.Guild.CurrentMember)) 
				await _music.JoinAsync(ctx.Member.VoiceState.Channel);
			
			if (!_music.IsInCurrentChannel(ctx.Member))
			{
				await ctx.RespondAsync("Sorry, but you have to be in the same channel to use music commands!");
				return;
			}
			
			if (ctx.Member.VoiceState.Channel.Type is not (ChannelType.Voice or ChannelType.Stage))
			{
				await ctx.RespondAsync("Uh?? You appear to be in an invalid channel.");
				return;
			}

			

			var stream = (await _ytClient.Videos.Streams.GetManifestAsync(video)).GetAudioOnlyStreams().First();
			
			_music._states[ctx.Guild.Id].Queue.Queue.Enqueue(new()
			{
				Requester = ctx.User,
				Duration = TimeSpan.FromSeconds((int)(stream.Bitrate.BitsPerSecond / 8 / stream.Size.Bytes)), 
				Stream = new(_htClientFactory.CreateSilkClient(), stream.Url, stream.Size.Bytes, !Regex.IsMatch(stream.Url, "ratebypass[=/]yes") ? 9_898_989 : null)
			});

			if (await _music.Play(ctx.Guild.Id))
				await ctx.Message.CreateReactionAsync(DiscordEmoji.FromUnicode("👍🏽"));
			else await ctx.RespondAsync("Oh no! Something went wrong while playing :(");
		}
	}
}