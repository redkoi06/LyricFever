using LyricFever.Core.Lyrics;
using LyricFever.Core.Providers;
using LyricFever.Core.Storage;

namespace LyricFever.Core.Tests;

public sealed class LyricFetchServiceTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"lyricfever-fetch-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task FetchAsync_ProviderTimeoutContinuesToNextProvider()
    {
        var successfulProvider = new StubProvider(
            new NetworkFetchReturn { Lyrics = [new LyricLine(1_000, "歌词")] });
        var service = new LyricFetchService(
            new LyricsRepository(new SqliteDatabase(_databasePath)),
            [new StubProvider(new TaskCanceledException("provider timeout")), successfulProvider]);

        var result = await service.FetchAsync("track-id", "Song", "Artist", "Album");

        Assert.Single(result.Lyrics);
        Assert.Equal("歌词", result.Lyrics[0].Words);
        Assert.Equal(1, successfulProvider.CallCount);
    }

    [Fact]
    public async Task FetchAsync_CallerCancellationStopsFallbackChain()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fallback = new StubProvider(new NetworkFetchReturn
        {
            Lyrics = [new LyricLine(1_000, "不应调用")]
        });
        var service = new LyricFetchService(
            new LyricsRepository(new SqliteDatabase(_databasePath)),
            [new StubProvider(new OperationCanceledException(cancellation.Token)), fallback]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.FetchAsync("track-id", "Song", "Artist", "Album", cancellation.Token));
        Assert.Equal(0, fallback.CallCount);
    }

    public void Dispose()
    {
        try { File.Delete(_databasePath); } catch { /* best-effort test cleanup */ }
    }

    private sealed class StubProvider : ILyricProvider
    {
        private readonly NetworkFetchReturn? _result;
        private readonly Exception? _exception;

        public StubProvider(NetworkFetchReturn result) => _result = result;
        public StubProvider(Exception exception) => _exception = exception;

        public string ProviderName => "Stub";
        public int CallCount { get; private set; }

        public Task<NetworkFetchReturn> FetchNetworkLyricsAsync(
            string trackName, string trackId, string? artistName, string? albumName,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _exception == null
                ? Task.FromResult(_result ?? NetworkFetchReturn.Empty)
                : Task.FromException<NetworkFetchReturn>(_exception);
        }

        public Task<List<SongResult>> SearchAsync(
            string trackName, string artistName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<SongResult>());
    }
}
