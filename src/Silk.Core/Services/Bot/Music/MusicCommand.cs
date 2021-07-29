using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using Silk.Core.Types;
using Silk.Core.Utilities.Bot;
using Silk.Core.Utilities.HttpClient;
using YoutubeExplode;
using YoutubeExplode.Playlists;
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
		
		
		[Command]
		[RequrieVC]
		[RequrieSameVC]
		[Priority(0)]
		public async Task Play(CommandContext ctx)
		{
			var result = await _music.PlayAsync(ctx.Guild.Id);

			if (result is MusicPlayResult.InvalidChannel) 
				await ctx.RespondAsync("I'm not in a channel!");
		}
		
		[Command]
		[RequrieVC]
		[RequrieSameVC]
		[Priority(2)]
		public async Task Play(CommandContext ctx, VideoId video)
		{
			string message;
			
			VoiceResult res = await _music.JoinAsync(ctx.Member.VoiceState.Channel);
			if (ctx.Guild.CurrentMember.VoiceState?.Channel is not null)
			{
				message = res switch
				{
					VoiceResult.Succeeded => $"Now connected to {ctx.Member.VoiceState.Channel.Mention}!",
					VoiceResult.SameChannel => "",
					VoiceResult.CannotUnsupress => "I'm not a stage moderator! Would you mind accepting my request to speak?",
					VoiceResult.CouldNotJoinChannel => "Awh. I can't join that channel!",
					VoiceResult.NonVoiceBasedChannel => "You...Don't seemt o be in a voice-based channel??",
				};
				
				if (res is not VoiceResult.SameChannel)
					await ctx.RespondAsync(message);
			}

			_music.Enqueue(ctx.Guild.Id, GetTrackAsync);
			
			if (res is (VoiceResult.CouldNotJoinChannel or VoiceResult.NonVoiceBasedChannel or VoiceResult.CannotUnsupress))
				return;
			
			var result = await _music.PlayAsync(ctx.Guild.Id);

			message = result switch
			{
				MusicPlayResult.NowPlaying => $"Now playing {_music.GetNowPlayingTitle(ctx.Guild.Id)}!",
				MusicPlayResult.AlreadyPlaying => "Queued 1 song.",
				_ => $"Unexpected response {result}"
			};

			await ctx.RespondAsync(message);

			async Task<MusicTrack> GetTrackAsync()
			{
				var manifest = await _ytClient.Videos.Streams.GetManifestAsync(video);
				var audio = manifest.GetAudioOnlyStreams().First();
				var stream = new LazyLoadHttpStream(_htClientFactory.CreateSilkClient(), audio.Url, audio.Size.Bytes, !Regex.IsMatch(audio.Url, "ratebypass[=/]yes") ? 9_898_989 : null);
				var vid = await _ytClient.Videos.GetAsync(video);

				return new()
				{
					Title = vid.Title,
					Stream = stream,
					Requester = ctx.User,
					Duration = vid.Duration.Value,
				};
			}
		}
	
		[Command]
		[RequrieVC]
		[RequrieSameVC]
		[Priority(1)]
		public async Task Play(CommandContext ctx, IReadOnlyList<PlaylistVideo> playlist)
		{
			var videos = playlist.Where(p => (p.Duration ?? TimeSpan.MaxValue) < TimeSpan.FromHours(4));
			foreach (PlaylistVideo video in videos)
			{
				_music.Enqueue(ctx.Guild.Id, () => GetTrackAsync(video));
			}

			await ctx.RespondAsync($"Queued {videos.Count()} videos.");
			
			async Task<MusicTrack> GetTrackAsync(PlaylistVideo video)
			{
				var manifest = await _ytClient.Videos.Streams.GetManifestAsync(video.Url);
				var audio = manifest.GetAudioOnlyStreams().First();
				var stream = new LazyLoadHttpStream(_htClientFactory.CreateSilkClient(), audio.Url, audio.Size.Bytes, !Regex.IsMatch(audio.Url, "ratebypass[=/]yes") ? 9_898_989 : null);
				var vid = await _ytClient.Videos.GetAsync(video.Url);
				
				return new()
				{
					Title = vid.Title,
					Stream = stream,
					Requester = ctx.User,
					Duration = vid.Duration.Value
				};
			}
		}
		
		[Command]
		[RequrieVC]
		[RequrieSameVC]
		public async Task Pause(CommandContext ctx) => _music.Pause(ctx.Guild.Id);

		[Command]
		[RequrieVC]
		[RequrieSameVC]
		public Task Resume(CommandContext ctx) => _music.ResumeAsync(ctx.Guild.Id).AsTask();

		[Command]
		[RequrieVC]
		[RequrieSameVC]
		public async Task Skip(CommandContext ctx)
		{
			var vstate = ctx.Member.VoiceState.Channel;
			var vstateUsers = vstate.Users.Count();
			if (vstateUsers < 4) // The bot + 2 others. //
			{
				await _music.SkipAsync(ctx.Guild.Id);
			}
			else
			{
				var interactivity = ctx.Client.GetInteractivity();
				var requiredVotes = vstateUsers * (2 / 3);
				var skip = await ctx.RespondAsync($"**Skip?** (Requires {vstateUsers - 1}/{vstateUsers} votes).");
				await skip.CreateReactionAsync(DiscordEmoji.FromUnicode("⏭"));

				var now = DateTime.UtcNow;
				var expiry = now + TimeSpan.FromSeconds(15);
				var votes = 0;

				while (now > expiry && votes >= requiredVotes)
				{
					var vote = await interactivity.WaitForReactionAsync(m => ((DiscordMember) m.User).VoiceState?.Channel == vstate, now - expiry);
					if (!vote.TimedOut)
						votes++;
				}

				if (votes >= requiredVotes)
					await _music.SkipAsync(ctx.Guild.Id);
			}
		}
	}
}