using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;

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
		/// <param name="voiceChannel">The channel to join.</param>
		/// <param name="commandChannel">The channel to send update messages to.</param>
		/// <returns>A <see cref="VoiceResult"/> with the result of trying to join.</returns>
		public async Task<VoiceResult> JoinAsync(DiscordChannel voiceChannel, DiscordChannel commandChannel)
		{
			if (voiceChannel.Type is not (ChannelType.Voice or ChannelType.Stage))
				return VoiceResult.NonVoiceBasedChannel;

			if (!voiceChannel.PermissionsFor(voiceChannel.Guild.CurrentMember).HasPermission(Permissions.Speak | Permissions.UseVoice))
				return VoiceResult.CouldNotJoinChannel;
			
			if (_states.TryGetValue(voiceChannel.Guild.Id, out var state) && state.ConnectedChannel == voiceChannel)
				return VoiceResult.SameChannel;
			
			var vnext = _client.GetShard(voiceChannel.Guild).GetVoiceNext();

			if (vnext.GetConnection(voiceChannel.Guild) is { } vnextConnection)
				vnextConnection.Disconnect();

			if (!voiceChannel.Guild.CurrentMember.IsDeafened)
			{
				try { await voiceChannel.Guild.CurrentMember.SetDeafAsync(true); }
				catch { }
			}
			
			
			var connection = await vnext.ConnectAsync(voiceChannel);
			
			state?.Dispose();
			state = _states[voiceChannel.Guild.Id] = new()
			{
				Connection =  connection,
				CommandChannel = commandChannel
			};

			state.TrackEnded += async (s, _) => await PlayAsync((s as MusicState)!.ConnectedChannel.Guild.Id);
			
			if (voiceChannel.Type is ChannelType.Stage)
			{
				try
				{
					await voiceChannel.UpdateCurrentUserVoiceStateAsync(false);
				}
				catch
				{
					await voiceChannel.UpdateCurrentUserVoiceStateAsync(true, DateTimeOffset.Now);
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
			
			Pause(guildId);

			await state.Queue.GetNextAsync();
			state.RestartFFMpeg();
			
			return await PlayAsync(guildId);
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
	
	public enum VoiceResult
	{
		Succeeded,
		SameChannel,
		CannotUnsupress,
		CouldNotJoinChannel,
		NonVoiceBasedChannel,
	}
}