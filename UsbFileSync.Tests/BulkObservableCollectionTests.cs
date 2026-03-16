using System.Collections.Specialized;
using UsbFileSync.App.Services;

namespace UsbFileSync.Tests;

public sealed class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_RaisesSingleResetChange()
    {
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        NotifyCollectionChangedEventArgs? changeArgs = null;
        var eventCount = 0;

        collection.CollectionChanged += (_, args) =>
        {
            eventCount++;
            changeArgs = args;
        };

        collection.ReplaceAll([4, 5]);

        Assert.Equal(1, eventCount);
        Assert.NotNull(changeArgs);
        Assert.Equal(NotifyCollectionChangedAction.Reset, changeArgs!.Action);
        Assert.Equal([4, 5], collection);
    }
}