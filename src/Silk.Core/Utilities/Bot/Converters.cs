using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;
using Silk.Extensions;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace Silk.Core.Utilities.Bot
{
    public class MemberConverter : IArgumentConverter<DiscordMember>
    {
        private static Regex UserRegex { get; } =
            new(@"^<@\!?(\d+?)>$", RegexOptions.ECMAScript | RegexOptions.Compiled);

        public async Task<Optional<DiscordMember>> ConvertAsync(string value, CommandContext ctx)
        {

            DiscordMember? user = ctx.Guild?.Members.Values.FirstOrDefault(m =>
                m.Nickname?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? m.Username.Contains(value, StringComparison.OrdinalIgnoreCase));
            if (user is not null) return Optional.FromValue(user);
            return await ConvertMemberAsync(value, ctx);
        }


        // Basically ripped from the source since we can't call this from the built-in one *shrug*

        private static async Task<Optional<DiscordMember>> ConvertMemberAsync(string value, CommandContext ctx)
        {
            if (ctx.Guild == null)
                return Optional.FromNoValue<DiscordMember>();

            if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong uid))
            {
                DiscordMember result = await ctx.Guild.GetMemberAsync(uid).ConfigureAwait(false);
                Optional<DiscordMember> ret =
                    result != null ? Optional.FromValue(result) : Optional.FromNoValue<DiscordMember>();
                return ret;
            }

            Match m = UserRegex.Match(value);
            if (m.Success && ulong.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out uid))
            {
                DiscordMember result = await ctx.Guild.GetMemberAsync(uid).ConfigureAwait(false);
                Optional<DiscordMember> ret =
                    result != null ? Optional.FromValue(result) : Optional.FromNoValue<DiscordMember>();
                return ret;
            }

            value = value.ToLowerInvariant();

            int di = value.IndexOf('#');
            string un = di != -1 ? value.Substring(0, di) : value;
            string? dv = di != -1 ? value.Substring(di + 1) : null;

            IEnumerable<DiscordMember>? us = ctx.Guild?.Members.Values
                .Where(xm =>
                    xm.Username.ToLowerInvariant() == un &&
                    (dv != null && xm.Discriminator == dv || dv == null)
                    || xm.Nickname?.ToLowerInvariant() == value);

            DiscordMember? mbr = us?.FirstOrDefault();
            return mbr != null ? Optional.FromValue(mbr) : Optional.FromNoValue<DiscordMember>();
        }
    }

    public sealed class TimeSpanConverter
    {
        private static Regex TimeSpanRegex { get; }

        static TimeSpanConverter() => TimeSpanRegex = new("^(?<days>\\d+d\\s*)?(?<hours>\\d{1,2}h\\s*)?(?<minutes>\\d{1,2}m\\s*)?(?<seconds>\\d{1,2}s\\s*)?$", RegexOptions.Compiled | RegexOptions.ECMAScript);

        public Task<Optional<TimeSpan>> ConvertAsync(string value)
        {
            if (value == "0")
                return Task.FromResult(Optional.FromValue(TimeSpan.Zero));

            if (int.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out int result1))
                return Task.FromResult(Optional.FromNoValue<TimeSpan>());

            value = value.ToLowerInvariant();

            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan result2))
                return Task.FromResult(Optional.FromValue(result2));

            string[] strArray1 =
            {
                "days",
                "hours",
                "minutes",
                "seconds"
            };

            Match match = TimeSpanRegex.Match(value);
            if (!match.Success)
                return Task.FromResult(Optional.FromNoValue<TimeSpan>());
            int days = 0;
            int hours = 0;
            int minutes = 0;
            int seconds = 0;
            for (result1 = 0; result1 < strArray1.Length; ++result1)
            {
                string groupname = strArray1[result1];
                string str = match.Groups[groupname].Value;
                if (!string.IsNullOrWhiteSpace(str))
                {
                    char ch = str[^1];
                    int.TryParse(str[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int result3);
                    switch (ch)
                    {
                        case 'd':
                            days = result3;
                            continue;
                        case 'h':
                            hours = result3;
                            continue;
                        case 'm':
                            minutes = result3;
                            continue;
                        case 's':
                            seconds = result3;
                            continue;
                        default:
                            continue;
                    }
                }
            }
            result2 = new(days, hours, minutes, seconds);
            return Task.FromResult(Optional.FromValue(result2));
        }
    }

    public sealed class VideoIdConverter : IArgumentConverter<Video>
    {
        public async Task<Optional<Video>> ConvertAsync(string value, CommandContext ctx)
        {
            var id = VideoId.TryParse(value);
            
            if (!id.HasValue)
                return Optional.FromNoValue<Video>();
            
            var yt = ctx.Services.Get<YoutubeClient>()!;
            
            var video = await yt.Videos.GetAsync(id.Value);
            
            
            
            return Optional.FromValue(video);
        }
    }

    public sealed class VideoPlaylistConverter : IArgumentConverter<IReadOnlyList<PlaylistVideo>>
    {
        public async Task<Optional<IReadOnlyList<PlaylistVideo>>> ConvertAsync(string value, CommandContext ctx)
        {
            if (PlaylistId.TryParse(value) is not {} video)
                return Optional.FromNoValue<IReadOnlyList<PlaylistVideo>>();

            var yt = ctx.Services.Get<YoutubeClient>()!;

            try
            {
                var playlist = await yt.Playlists.GetVideosAsync(video);
                return Optional.FromValue<IReadOnlyList<PlaylistVideo>>(playlist);
            }
            catch
            {
                return Optional.FromNoValue<IReadOnlyList<PlaylistVideo>>();
            }
        }
    }
    
}