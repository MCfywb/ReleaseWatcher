using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Exceptions;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.Application.Tests;

/// <summary>
/// <see cref="IReleaseFeed"/> 的手写替身。
/// </summary>
/// <remarks>
/// 手写而非引入 Mock 框架：该接口只有一个方法、语义清晰，
/// 替身的价值在于"可编程地返回不同场景"，不需要断言次数之类的能力。
/// 少一个依赖，也少一处版本冲突来源。
/// </remarks>
internal sealed class FakeReleaseFeed : IReleaseFeed
{
    private readonly IReadOnlyList<Release> _releases;

    public FakeReleaseFeed(IReadOnlyList<Release>? releases = null)
    {
        _releases = releases ?? Array.Empty<Release>();
    }

    /// <summary>被调用次数，用于验证用例只取数一次。</summary>
    public int CallCount { get; private set; }

    /// <summary>最近一次传入的仓库。</summary>
    public RepositoryRef? LastRequestedRepository { get; private set; }

    /// <summary>设为非空时，<see cref="GetReleasesAsync"/> 抛出该异常。</summary>
    public ReleaseFeedException? ThrowOnCall { get; init; }

    public Task<IReadOnlyList<Release>> GetReleasesAsync(
        RepositoryRef repository,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequestedRepository = repository;

        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        return Task.FromResult(_releases);
    }
}
