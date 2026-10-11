using System.ComponentModel;
using System.Windows;

using ReleaseWatcher.App.ViewModels;
using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Services;
using ReleaseWatcher.Domain.Entities;
using ReleaseWatcher.Domain.Exceptions;
using ReleaseWatcher.Domain.ValueObjects;

namespace ReleaseWatcher.App.Tests;

/// <summary>
/// <see cref="RepositoryListViewModel"/> 中"空态 ↔ 列表"互斥可见性的通知验证。
/// </summary>
/// <remarks>
/// 这对可见性直接驱动三栏 Grid（<c>ReposSplit</c>）的显隐。只要其中一个
/// 赋值时不发 <c>PropertyChanged</c>，三栏就会停在收起态，界面表现为
/// 检测完成后整块空白 —— 2026-10-12 用户截图即此故障。
/// </remarks>
public class RepositoryListVisibilityTests
{
    private sealed class StubFeed : IReleaseFeed
    {
        public Task<IReadOnlyList<Release>> GetReleasesAsync(
            RepositoryRef repository,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Release> releases =
            [
                Release.FromTag(repository, "v2.0.0", title: "2.0.0"),
                Release.FromTag(repository, "v1.0.0", title: "1.0.0"),
            ];

            return Task.FromResult(releases);
        }
    }

    private static RepositoryRef Repo()
        => RepositoryRef.TryParse("dotnet/runtime", out var r)
            ? r!
            : throw new InvalidOperationException("测试数据构造失败");

    private static RepositoryListViewModel CreateViewModel()
    {
        var feed = new StubFeed();
        return new RepositoryListViewModel(
            feed,
            new ReleaseDetectionService(feed),
            () => [Repo()],
            _ => SemanticVersion.TryParse("1.0.0", out var v) ? v : null);
    }

    [Fact]
    public async Task Refresh_WithRows_MustNotifyListVisibility()
    {
        var vm = CreateViewModel();

        var notified = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // 初态：无仓库 → 空态可见、三栏收起
        Assert.Equal(Visibility.Visible, vm.EmptyStateVisibility);
        Assert.Equal(Visibility.Collapsed, vm.ListVisibility);

        await vm.Refresh();

        // 检测到仓库后应切换为"列表可见、三栏展开"
        Assert.Equal(Visibility.Collapsed, vm.EmptyStateVisibility);
        Assert.Equal(Visibility.Visible, vm.ListVisibility);

        // 关键断言：ListVisibility 必须发出通知，否则界面收不到"变可见"
        Assert.Contains(nameof(RepositoryListViewModel.ListVisibility), notified);
    }
}