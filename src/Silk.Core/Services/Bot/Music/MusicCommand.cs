using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Silk.Core.Types;
using Silk.Core.Utilities.Bot;
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
		
		
		[Command]
		[RequrieVC]
		[Priority(0)]
		public async Task Play(CommandContext ctx)
		{
			var result = await _music.PlayAsync(ctx.Guild.Id);

			if (result is MusicPlayResult.InvalidChannel) 
				await ctx.RespondAsync("I'm not in a channel!");
		}
		
		[Command]
		[RequrieVC]
		[Priority(2)]
		public async Task Play(CommandContext ctx, VideoId video)
		{
			string message;
			
			VoiceResult res = await _music.JoinAsync(ctx.Member.VoiceState.Channel);
			if (ctx.Guild.CurrentMember.VoiceState?.Channel is null)
			{
				message = res switch
				{
					VoiceResult.Succeeded => $"Now connected to {ctx.Member.VoiceState.Channel.Mention}!",
					VoiceResult.SameChannel => "We're...Already in the same channel.",
					VoiceResult.CannotUnsupress => "I managed to join, but not speak.",
					VoiceResult.CouldNotJoinChannel => "Awh. I can't join that channel!",
					VoiceResult.NonVoiceBasedChannel => "You...Don't seemt o be in a voice-based channel??"
				};

				await ctx.RespondAsync(message);
			
				if (res is not VoiceResult.Succeeded or VoiceResult.SameChannel)
					return;
			}

			_music.Enqueue(ctx.Guild.Id, GetTrackAsync);
			
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
			};
		}
	}
}