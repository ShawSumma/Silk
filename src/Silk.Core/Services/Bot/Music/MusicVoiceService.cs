using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;

namespace Silk.Core.Services.Bot.Music
{
	public sealed class MusicVoiceService
	{
		public readonly ConcurrentDictionary<ulong, MusicVoiceState> _states = new();
		private readonly DiscordShardedClient _client;
		public MusicVoiceService(DiscordShardedClient client)
		{
			_client = client;
		}

		public bool IsInChannel(DiscordMember member) => member.VoiceState?.Channel is not null;
		public bool IsInCurrentChannel(DiscordMember member) => IsInChannel(member) && _states.TryGetValue(member.Guild.Id, out var state) && state.ConnectedChannel == member.VoiceState.Channel;
		
		
		public async Task<bool> JoinAsync(DiscordChannel channel)
		{
			if (_states.TryGetValue(channel.Guild.Id, out var state))
				if (state.ConnectedChannel == channel)
					return false;

			state ??= _states[channel.Guild.Id] = new() {VNextExtension = _client.GetShard(channel.Guild.Id).GetVoiceNext()};
			
			if (state.ConnectedChannel is not null)
				state.VNextConnection!.Disconnect();
			
			try
			{
				state.VNextConnection = await state.VNextExtension!.ConnectAsync(channel);
				await channel.Guild.CurrentMember.SetDeafAsync(true);
				return true;
			}
			catch
			{
				return false;
			}
		}

		public async Task<bool> Play(ulong guildId)
		{
			if (!_states.TryGetValue(guildId, out var state))
				return false;

			if (state.Queue.NowPlaying is null && !state.Queue.GetNext())
				return false;

			if (state.Queue.RemainingDuration <= TimeSpan.Zero)
				state.Queue.GetNext();
		
			if (state.FFMpeg is null)
				state.StartFFMpeg();
			
			var vnext = state.VNextConnection!;
			var sink = vnext.GetTransmitSink();

			try
			{
				await Task.WhenAll(state.Queue.NowPlaying!.Stream.CopyToAsync(state.FFMpegIn!, state.Token), state.FFMpegOut.CopyToAsync(sink, cancellationToken: state.Token));
				await state.VNextConnection!.GetTransmitSink().FlushAsync();
			}
			catch { }

			return true;
		}
	}

	public sealed record MusicVoiceState
	{
		public bool IsPaused { get; set; }

		public MusicQueue Queue { get; } = new();

		public VoiceNextExtension VNextExtension { get; init; }
		public VoiceNextConnection? VNextConnection { get; set; }
		public DiscordChannel? ConnectedChannel => VNextConnection?.TargetChannel;

		private CancellationTokenSource _cts = new();
		public CancellationToken Token => _cts.Token;
		
		
		public Process? FFMpeg { get; private set; }
		public Stream? FFMpegIn => FFMpeg?.StandardInput.BaseStream;
		public Stream? FFMpegOut => FFMpeg?.StandardOutput.BaseStream;
		
		
		private readonly ProcessStartInfo _ffmpegInfo = new()
		{
			Arguments = "-i - -ac 2 -f s16le -ar 48000 pipe:1 -fflags nobuffer",
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			FileName =
				OperatingSystem.IsLinux() ? "./ffmpeg-linux" :
				OperatingSystem.IsWindows() ? "./ffmpeg-windows" :
				throw new PlatformNotSupportedException(),
			CreateNoWindow = true,
			UseShellExecute = false,
		};

		public void Pause()
		{
			if (IsPaused)
				return;
			
			IsPaused = true;
			
			_cts.Cancel();
			_cts.Dispose();
			_cts = new();
		}

		public void Resume() => IsPaused = false;
		
		public void StartFFMpeg() => FFMpeg = Process.Start(_ffmpegInfo);
		public void StopFFMpeg()
		{
			if (FFMpeg?.HasExited ?? true)
				return;
			
			FFMpeg.Kill();
			FFMpeg.Dispose();
			FFMpeg = null;
		}
	}
}