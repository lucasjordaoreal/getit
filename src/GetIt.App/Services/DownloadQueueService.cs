using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GetIt_App.ViewModels;

namespace GetIt_App.Services;

public sealed class DownloadQueueService
{
    private readonly SemaphoreSlim _downloadAllLock = new(1, 1);
    private CancellationTokenSource? _globalCts;

    public async Task DownloadAllAsync(IList<DownloadItemViewModel> queue, string destinationFolder)
    {
        if (queue.Count == 0)
        {
            return;
        }

        await _downloadAllLock.WaitAsync();
        try
        {
            if (queue.All(item => !item.HasMetadata || item.IsDownloadComplete || item.IsDownloading))
            {
                return;
            }

            foreach (var item in queue)
            {
                item.IsQueueLocked = true;
            }

            _globalCts?.Cancel();
            _globalCts?.Dispose();
            _globalCts = new CancellationTokenSource();

            try
            {
                foreach (var item in queue.ToList())
                {
                    if (_globalCts.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (item.HasMetadata && !item.IsDownloadComplete && !item.IsDownloading)
                    {
                        await item.StartDownloadAsync(destinationFolder, _globalCts.Token);
                    }
                }
            }
            finally
            {
                foreach (var item in queue)
                {
                    item.IsQueueLocked = false;
                }

                _globalCts?.Cancel();
                _globalCts?.Dispose();
                _globalCts = null;
            }
        }
        finally
        {
            _downloadAllLock.Release();
        }
    }

    public void CancelAll(IList<DownloadItemViewModel> queue)
    {
        _globalCts?.Cancel();
        foreach (var item in queue)
        {
            item.CancelCurrentOperation();
        }
    }

    public void ClearQueue(IList<DownloadItemViewModel> queue)
    {
        CancelAll(queue);
        queue.Clear();
    }
}
