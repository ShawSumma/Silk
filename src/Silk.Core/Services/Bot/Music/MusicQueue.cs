using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Silk.Core.Services.Bot.Music
{
	public sealed record MusicQueue
	{
		public MusicTrack? NowPlaying => _nowPlaying;
		private MusicTrack? _nowPlaying;

		public TimeSpan RemainingDuration => TimeSpan.FromSeconds(RemainingSeconds);
		internal int RemainingSeconds { get; set; }
		
		public int RemainingTracks => _queue.Count;

		private readonly ConcurrentQueue<Lazy<Task<MusicTrack>>> _queue = new();

		public void Enqueue(Func<Task<MusicTrack>> queueFunc) => _queue.Enqueue(new(queueFunc));
		
		public async Task<bool> GetNextAsync()
		{
			var dequeued = _queue.TryDequeue(out var npLazy);

			if (dequeued)
			{
				_nowPlaying = await npLazy!.Value;
				RemainingSeconds = (int)_nowPlaying.Duration.TotalSeconds;
			}
			
			return dequeued;
		}
	}
}