using System;
using System.Collections.Generic;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Api.Models.LiveTvDtos;

/// <summary>
/// Channel mapping options dto.
/// </summary>
public class ChannelMappingOptionsDto
{
    /// <summary>
    /// Gets or sets list of tuner channels.
    /// </summary>
    required public IReadOnlyList<TunerChannelMapping> TunerChannels { get; set; }

    /// <summary>
    /// Gets or sets list of provider channels.
    /// </summary>
    required public IReadOnlyList<NameIdPair> ProviderChannels { get; set; }

    /// <summary>
    /// Gets or sets list of mappings.
    /// </summary>
    public IReadOnlyList<NameValuePair> Mappings { get; set; } = Array.Empty<NameValuePair>();

    /// <summary>
    /// Gets or sets provider name.
    /// </summary>
    public string? ProviderName { get; set; }
}
