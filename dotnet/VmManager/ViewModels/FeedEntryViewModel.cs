using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VmManager.ViewModels;

public partial class FeedEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOci))]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    private FeedType _type = FeedType.OCI;

    public bool IsOci => Type == FeedType.OCI;

    public bool IsLocal => Type == FeedType.Local;

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private string _repository = "";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _connectionStatus = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private ObservableCollection<string> _discoveredRepos = new ObservableCollection<string>();

    public FeedConfiguration ToConfiguration()
    {
        return new FeedConfiguration
        {
            Id = FeedConfiguration.ComputeId(Type, Url, Repository),
            Name = Name,
            Type = Type,
            Url = Url,
            Repository = Repository,
            Username = Username,
            Password = Password,
        };
    }

    public static FeedEntryViewModel FromConfiguration(FeedConfiguration feed)
    {
        return new FeedEntryViewModel
        {
            Name = feed.Name,
            Type = feed.Type,
            Url = feed.Url,
            Repository = feed.Repository ?? "",
            Username = feed.Username ?? "",
            Password = feed.Password ?? "",
        };
    }
}
