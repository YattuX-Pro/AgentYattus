using System.Net;
using System.Runtime.CompilerServices;
using AgentYattus.Api.Modules.Llm;
using Microsoft.Extensions.AI;

namespace AgentYattus.Api.Tests;

public sealed class StreamingFallbackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_chunk_arrives_before_the_provider_finishes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new StreamingClient(release.Task);
        using var chain = new FallbackChatClient([new(provider, "first")]);
        await using var stream = chain.GetStreamingResponseAsync([new(ChatRole.User, "Bonjour")], cancellationToken: Ct).GetAsyncEnumerator(Ct);
        Assert.True(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.Equal("Bon", stream.Current.Text);
        Assert.False(release.Task.IsCompleted);
        release.SetResult();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("jour", stream.Current.Text);
        Assert.False(await stream.MoveNextAsync());
    }

    [Fact]
    public async Task A_quota_error_before_the_first_chunk_switches_to_the_next_stream()
    {
        using var chain = new FallbackChatClient([
            new(new StreamingClient(Task.CompletedTask, failBefore: true), "exhausted"),
            new(new StreamingClient(Task.CompletedTask), "available")]);
        var text = "";
        await foreach (var update in chain.GetStreamingResponseAsync([new(ChatRole.User, "Bonjour")], cancellationToken: Ct))
            text += update.Text;
        Assert.Equal("Bonjour", text);
        Assert.Equal(["exhausted", "available"], chain.Used);
    }

    [Fact]
    public async Task A_failure_after_a_chunk_does_not_mix_another_models_answer_into_the_stream()
    {
        using var chain = new FallbackChatClient([
            new(new StreamingClient(Task.CompletedTask, failAfter: true), "first"),
            new(new StreamingClient(Task.CompletedTask), "unused")]);
        await using var stream = chain.GetStreamingResponseAsync([new(ChatRole.User, "Bonjour")], cancellationToken: Ct).GetAsyncEnumerator(Ct);
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal("Bon", stream.Current.Text);
        await Assert.ThrowsAsync<HttpRequestException>(() => stream.MoveNextAsync().AsTask());
        Assert.Equal(["first"], chain.Used);
    }

    [Fact]
    public async Task A_silent_provider_is_replaced_without_waiting_for_its_network_timeout()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var chain = new FallbackChatClient([
            new(new StreamingClient(never.Task, waitBefore: true), "silent"),
            new(new StreamingClient(Task.CompletedTask), "available")], TimeSpan.FromMilliseconds(100));
        var text = "";
        await foreach (var update in chain.GetStreamingResponseAsync([new(ChatRole.User, "Bonjour")], cancellationToken: Ct))
            text += update.Text;
        Assert.Equal("Bonjour", text);
        Assert.Equal(["silent", "available"], chain.Used);
    }

    [Fact]
    public async Task User_cancellation_does_not_start_another_provider()
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var chain = new FallbackChatClient([
            new(new StreamingClient(never.Task, waitBefore: true), "first"),
            new(new StreamingClient(Task.CompletedTask), "unused")]);
        await using var stream = chain.GetStreamingResponseAsync([new(ChatRole.User, "Bonjour")], cancellationToken: cancelled.Token).GetAsyncEnumerator(cancelled.Token);
        var pending = stream.MoveNextAsync().AsTask();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(["first"], chain.Used);
    }

    internal sealed class StreamingClient(Task release, bool failBefore = false, bool failAfter = false, bool waitBefore = false) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Le chemin sans streaming ne doit pas être utilisé.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (waitBefore) await release.WaitAsync(cancellationToken);
            if (failBefore) throw new HttpRequestException("quota", null, HttpStatusCode.TooManyRequests);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Bon");
            await release.WaitAsync(cancellationToken);
            if (failAfter) throw new HttpRequestException("quota", null, HttpStatusCode.TooManyRequests);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "jour");
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
