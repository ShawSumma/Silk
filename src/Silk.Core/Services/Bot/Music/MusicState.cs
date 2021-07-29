using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using Emzi0767.Utilities;

namespace Silk.Core.Services.Bot.Music
{
	internal sealed record MusicState : IDisposable
	{
		public MusicQueue Queue { get; } = new();
		public MusicTrack? NowPlaying => Queue.NowPlaying;
		public TimeSpan RemainingDuration => Queue.RemainingDuration;
	
		
		public VoiceNextConnection Connection { get; init; }
		
		public DiscordChannel CommandChannel { get; init; }
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
			Arguments = "-hide_banner -loglevel quiet -i - -ac 2 -f s16le -ar 48k pipe:1 ",
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			FileName =
				OperatingSystem.IsLinux() ? "./ffmpeg" : //Linux is just ffmpeg
				OperatingSystem.IsWindows() ? "./ffmpeg-windows" :
				throw new PlatformNotSupportedException(),
			CreateNoWindow = true,
			UseShellExecute = false,
		};
		private readonly TimeSpan _preloadBuffer = TimeSpan.FromSeconds(10);
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
				
				if (RemainingDuration < _preloadBuffer && !trackLoaded)
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
}