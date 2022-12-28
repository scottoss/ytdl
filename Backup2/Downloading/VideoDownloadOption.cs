﻿using System;
using System.Collections.Generic;
using System.Linq;
using YoutubeDownloader.Core.Utils;
using YoutubeDownloader.Core.Utils.Extensions;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Downloading;

public partial record VideoDownloadOption(Container Container, IReadOnlyList<IStreamInfo> StreamInfos)
{
    public VideoQuality? VideoQuality => Memo.Cache(this, () =>
        StreamInfos
            .OfType<IVideoStreamInfo>()
            .Select(s => s.VideoQuality)
            .OrderByDescending(q => q)
            .FirstOrDefault()
    );
}

public partial record VideoDownloadOption
{
    internal static IReadOnlyList<VideoDownloadOption> ResolveAll(StreamManifest manifest)
    {
        IEnumerable<VideoDownloadOption> GetVideoAndAudioOptions()
        {
            var videoStreams = manifest
                .GetVideoStreams()
                .OrderByDescending(v => v.VideoQuality);

            foreach (var videoStreamInfo in videoStreams)
            {
                // Muxed stream
                if (videoStreamInfo is MuxedStreamInfo)
                {
                    yield return new VideoDownloadOption(
                        videoStreamInfo.Container,
                        new[] { videoStreamInfo }
                    );
                }
                // Separate audio + video stream
                else
                {
                    // Prefer audio stream with the same container
                    var audioStreamInfo = manifest
                        .GetAudioStreams()
                        .OrderByDescending(s => s.Container == videoStreamInfo.Container)
                        .ThenByDescending(s => s is AudioOnlyStreamInfo)
                        .ThenByDescending(s => s.Bitrate)
                        .FirstOrDefault();

                    if (audioStreamInfo is not null)
                    {
                        yield return new VideoDownloadOption(
                            videoStreamInfo.Container,
                            new IStreamInfo[] { videoStreamInfo, audioStreamInfo }
                        );
                    }
                }
            }
        }

        IEnumerable<VideoDownloadOption> GetAudioOnlyOptions()
        {
            // WebM-based audio-only containers
            {
                var audioStreamInfo = manifest
                    .GetAudioStreams()
                    .OrderByDescending(s => s.Container == Container.WebM)
                    .ThenByDescending(s => s is AudioOnlyStreamInfo)
                    .ThenByDescending(s => s.Bitrate)
                    .FirstOrDefault();

                if (audioStreamInfo is not null)
                {
                    yield return new VideoDownloadOption(new Container("mp3"), new[] { audioStreamInfo });
                    yield return new VideoDownloadOption(new Container("ogg"), new[] { audioStreamInfo });
                }
            }

            // Mp4-based audio-only containers
            {
                var audioStreamInfo = manifest
                    .GetAudioStreams()
                    .OrderByDescending(s => s.Container == Container.Mp4)
                    .ThenByDescending(s => s is AudioOnlyStreamInfo)
                    .ThenByDescending(s => s.Bitrate)
                    .FirstOrDefault();

                if (audioStreamInfo is not null)
                {
                    yield return new VideoDownloadOption(new Container("m4a"), new[] { audioStreamInfo });
                }
            }
        }

        // Deduplicate download options by video quality and container
        var comparer = new DelegateEqualityComparer<VideoDownloadOption>(
            (x, y) => x.VideoQuality == y.VideoQuality && x.Container == y.Container,
            x => HashCode.Combine(x.VideoQuality, x.Container)
        );

        var options = new HashSet<VideoDownloadOption>(comparer);

        options.AddRange(GetVideoAndAudioOptions());
        options.AddRange(GetAudioOnlyOptions());

        return options.ToArray();
    }
}