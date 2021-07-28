using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using Emzi0767.Utilities;
using Serilog;

namespace Silk.Core.Services.Bot.Music
{
	public sealed class MusicVoiceService
	{
		/// <summary>
		/// The states of any given guild.
		/// </summary>
		private readonly ConcurrentDictionary<ulong, MusicState> _states = new();

		private readonly SemaphoreSlim _lock = new(1);
		
		private readonly DiscordShardedClient _client;
		public MusicVoiceService(DiscordShardedClient client)
		{
			_client = client;
		}

		public string GetNowPlayingTitle(ulong guildId)
		{
			if (!_states.TryGetValue(guildId, out var state))
				return "Nothing.";

			if (state.NowPlaying is null)
				return "Nothing.";

			else return $"**{state.NowPlaying.Title}**";
		}
		
		/// <summary>
		/// Joins a new channel.
		/// </summary>
		/// <param name="channel">The channel to join.</param>
		/// <returns>A <see cref="VoiceResult"/> with the result of trying to join.</returns>
		public async Task<VoiceResult> JoinAsync(DiscordChannel channel)
		{
			if (channel.Type is not (ChannelType.Voice or ChannelType.Stage))
				return VoiceResult.NonVoiceBasedChannel;

			if (!channel.PermissionsFor(channel.Guild.CurrentMember).HasPermission(Permissions.Speak | Permissions.UseVoice))
				return VoiceResult.CouldNotJoinChannel;
			
			if (_states.TryGetValue(channel.Guild.Id, out var state) && state.ConnectedChannel == channel)
				return VoiceResult.SameChannel;
			
			var vnext = _client.GetShard(channel.Guild).GetVoiceNext();

			if (vnext.GetConnection(channel.Guild) is { } vnextConnection)
				vnextConnection.Disconnect();

			if (!channel.Guild.CurrentMember.IsDeafened)
			{
				try { await channel.Guild.CurrentMember.SetDeafAsync(true); }
				catch { }
			}
			
			
			var connection = await vnext.ConnectAsync(channel);
			
			var trackstate = _states[channel.Guild.Id] = new()
			{
				Connection =  connection
			}; // TODO: Dispose

			trackstate.TrackEnded += async (s, _) =>
			{
				var res = await PlayAsync((s as MusicState)!.ConnectedChannel.Guild.Id);
				Log.Information("AutoPlay returned {Result}", res);
			};
			
			if (channel.Type is ChannelType.Stage)
			{
				try
				{
					await channel.UpdateCurrentUserVoiceStateAsync(false);
				}
				catch
				{
					await channel.UpdateCurrentUserVoiceStateAsync(true, DateTimeOffset.Now);
					return VoiceResult.CannotUnsupress;
				}
			}
			
			
			return VoiceResult.Succeeded;
		}
		
		public async Task<MusicPlayResult> PlayAsync(ulong guildId)
		{
			await _lock.WaitAsync();

			try
			{
				if (!_states.TryGetValue(guildId, out var state))
					return MusicPlayResult.InvalidChannel;

				if (state.IsPlaying && state.NowPlaying is not null)
					return MusicPlayResult.AlreadyPlaying;
				
				if (state.NowPlaying is null || state.RemainingDuration <= TimeSpan.Zero)
					if (!await state.Queue.GetNextAsync())
						return MusicPlayResult.QueueEmpty;

				var vnextSink = state.Connection.GetTransmitSink();
			
				await state.ResumeAsync();
				Task yt = state.NowPlaying!.Stream.CopyToAsync(state.InStream, state.Token);
				Task vn = state.OutStream.CopyToAsync(vnextSink, cancellationToken: state.Token);
			
				_ = Task.Run(async () => await Task.WhenAll(yt, vn));
			
				return MusicPlayResult.NowPlaying;
			}
			finally
			{
				_lock.Release();
			}
		}

		public async Task<MusicPlayResult> SkipAsync(ulong guildId)
		{
			if (!_states.TryGetValue(guildId, out var state))
				return MusicPlayResult.InvalidChannel;

			if (state.Queue.RemainingTracks is 0)
				return MusicPlayResult.QueueEmpty;
			
			Pause(guildId);

			await state.Queue.GetNextAsync();
			state.RestartFFMpeg();
			
			await PlayAsync(guildId);
			
			return MusicPlayResult.NowPlaying;
		}

		public void Pause(ulong guildId)
		{
			if (!_states.TryGetValue(guildId, out var state))
				return;
			
			if (!state.IsPlaying)
				return;
			
			state.Pause();
		}

		public async ValueTask ResumeAsync(ulong guildId)
		{
			if (!_states.TryGetValue(guildId, out var state))
				return;

			if (!state.IsPlaying) // Don't needlessly yeet the CT. //
				return;
			
			var vnextSink = state.Connection.GetTransmitSink();
			
			await state.ResumeAsync();
			Task yt = state.NowPlaying!.Stream.CopyToAsync(state.InStream, state.Token);
			Task vn = state.OutStream.CopyToAsync(vnextSink, cancellationToken: state.Token);
			
			_ = Task.Run(async () => await Task.WhenAll(yt, vn));
		}

		public void Enqueue(ulong guildId, Func<Task<MusicTrack>> fun)
		{
			if (!_states.TryGetValue(guildId, out var state))
				return;
			
			state.Queue.Enqueue(fun);
		}
	}

	internal sealed record MusicState : IDisposable
	{
		public MusicQueue Queue { get; } = new();
		public MusicTrack? NowPlaying => Queue.NowPlaying;
		public TimeSpan RemainingDuration => Queue.RemainingDuration;
	
		
		public VoiceNextConnection Connection { get; init; }
		public DiscordChannel ConnectedChannel => Connection.TargetChannel;

		public CancellationToken Token => _cts.Token;
		private CancellationTokenSource _cts = new();

		public bool IsPlaying => _mre.IsSet;

		private readonly AsyncManualResetEvent _mre = new(false);

		public FileStream InStream => (FileStream)_ffmpeg.StandardInput.BaseStream;
		public FileStream OutStream => (FileStream)_ffmpeg.StandardOutput.BaseStream;
		
		private Process _ffmpeg;
		private readonly ProcessStartInfo _ffmpegInfo = new()
		{
			Arguments = "-i - -ac 2 -f s16le -ar 48k pipe:1",
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			FileName =
				OperatingSystem.IsLinux() ? "./ffmpeg-linux" :
				OperatingSystem.IsWindows() ? "./ffmpeg-windows" :
				throw new PlatformNotSupportedException(),
			CreateNoWindow = true,
			UseShellExecute = false,
		};
		private readonly TimeSpan _tenSecondBuffer = TimeSpan.FromSeconds(10);
		private bool _disposing;

		public MusicState()
		{
			RestartFFMpeg();
			DoDurationLoopAsync();
		}

		public Task ResumeAsync() => _mre.SetAsync();

		public event EventHandler TrackEnded;
		
		public void Pause()
		{
			_mre.Reset();
			_cts.Cancel();
			_cts.Dispose();
			_cts = new();
		}

		public void RestartFFMpeg()
		{
			_ffmpeg?.Kill();
			_ffmpeg?.Dispose();
			_ffmpeg = Process.Start(_ffmpegInfo)!;
		}

		private async void DoDurationLoopAsync()
		{
			bool trackLoaded = false;
			while (!_disposing)
			{
				await _mre.WaitAsync();
			
				// The reson CT.None is passed is because pausing and resuming sets the MRE,
				// so we don't want to cancel, becasue we're going to wait anyway.
				await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
				
				if (RemainingDuration < _tenSecondBuffer && !trackLoaded)
				{
					if (Queue.RemainingTracks is not 0)
					{
						await Queue.PreloadAsync();
						trackLoaded = true;
					}
				}

				if (RemainingDuration <= TimeSpan.Zero)
				{
					Pause();
					RestartFFMpeg(); // FFMpeg(?) cuts off the beginning of the song after the first song otherwise. //
					trackLoaded = false;
					TrackEnded(this, EventArgs.Empty); //TODO: Make custom handler 
				}
				
				Queue.RemainingSeconds--;
			}
		}

		~MusicState() => Dispose();
		
		public void Dispose()
		{
			if (_disposing)
				throw new ObjectDisposedException(this.GetType().Name, "This object is already disposed.");
			_disposing = true;
			GC.SuppressFinalize(this);
			_cts.Dispose();
			_ffmpeg.Dispose();
			Connection.Dispose();
		}
	}

	public enum VoiceResult
	{
		Succeeded,
		SameChannel,
		CannotUnsupress,
		CouldNotJoinChannel,
		NonVoiceBasedChannel,
	}
}