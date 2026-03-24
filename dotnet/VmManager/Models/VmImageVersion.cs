using CommunityToolkit.Mvvm.ComponentModel;

namespace VmManager.Models;

/// <summary>A specific version of a catalog VM image.</summary>
public partial class VmImageVersion : ObservableObject
{
    public string Version { get; set; } = "";

    /// <summary>File name relative to the catalog network path, e.g. "crywinbase-1.0.0.box".</summary>
    public string FileName { get; set; } = "";

    public double SizeGb { get; set; }
    public DateTime Date { get; set; }
    public string Notes { get; set; } = "";

    /// <summary>
    /// True once the box has been downloaded and extracted locally.
    /// Set by the ViewModel after checking disk on catalog load.
    /// </summary>
    [ObservableProperty]
    private bool _isLocallyAvailable;
}
