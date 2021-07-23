using System;
using System.Collections.Concurrent;

namespace Silk.Core.Services.Bot.Music
{
	public sealed record MusicQueue
	{
		public MusicTrack? NowPlaying => _nowPlaying;
		private MusicTrack? _nowPlaying;
		
		public TimeSpan RemainingDuration => _nowPlaying is null ? TimeSpan.Zero : 
			TimeSpan.FromMilliseconds((int)Math.Ceiling(_nowPlaying.Duration.TotalMilliseconds - ((_nowPlaying.Stream.Length /*bytes*/ / _nowPlaying.Duration.TotalMilliseconds) * _nowPlaying.Stream.Position)));

		
		public ConcurrentQueue<MusicTrack> Queue { get; } = new();

		public bool GetNext() => Queue.TryDequeue(out _nowPlaying);
	}
}