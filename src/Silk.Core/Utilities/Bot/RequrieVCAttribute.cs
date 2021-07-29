using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace Silk.Core.Utilities.Bot
{
	/// <summary>
	/// Requires a member be in a voice channel.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
	public class RequrieVCAttribute : CheckBaseAttribute
	{
		public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
		{
			if (help)
				return true;

			return ctx.Member.VoiceState?.Channel is not null;
		}
	}
	
	/// <summary>
	/// Requires a member be in a voice channel.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
	public class RequrieSameVCAttribute : CheckBaseAttribute
	{
		public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
		{
			if (help)
				return true;

			return ctx.Member.VoiceState?.Channel is {} tc && tc == ctx.Guild.CurrentMember.VoiceState?.Channel;
		}
	}
}