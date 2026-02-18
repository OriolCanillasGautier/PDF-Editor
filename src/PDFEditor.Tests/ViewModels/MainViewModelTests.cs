using Avalonia.Threading;
using PDFEditor.UI.ViewModels;
using PDFEditor.Tests.Infrastructure;
using ReactiveUI;
using System.Reactive;
using Xunit;

namespace PDFEditor.Tests.ViewModels;

/// <summary>
/// Unit tests for MainViewModel — app-level state: tabs, theme, recent files.
/// Runs against a headless Avalonia environment so AvaloniaScheduler is valid.
/// </summary>
[Collection("AvaloniaTests")]
public class MainViewModelTests
{
    // ----------------------------------------------------------
    // Initial State
    // ----------------------------------------------------------

    [Fact]
    public void Constructor_InitialisesEmptyTabs()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Tabs);
    }

    [Fact]
    public void Constructor_ActiveTab_IsNull()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.ActiveTab);
    }

    [Fact]
    public void HasActiveTab_WhenNoTab_IsFalse()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HasActiveTab);
    }

    [Fact]
    public void AppStatus_Default_IsReady()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Ready", vm.AppStatus);
    }

    [Fact]
    public void IsDarkTheme_Default_IsFalse()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        // Default when no session file exists is false
        Assert.False(vm.IsDarkTheme);
    }

    // ----------------------------------------------------------
    // Theme Toggle
    // ----------------------------------------------------------

    [Fact]
    public void ToggleThemeCommand_FlipsIsDarkTheme()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        bool initial = vm.IsDarkTheme;
        vm.ToggleThemeCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(initial, vm.IsDarkTheme);
    }

    [Fact]
    public void ToggleThemeCommand_ToggledTwice_RestoresOriginal()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        bool initial = vm.IsDarkTheme;
        vm.ToggleThemeCommand.Execute().Subscribe();
        vm.ToggleThemeCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(initial, vm.IsDarkTheme);
    }

    [Fact]
    public void IsDarkTheme_SetDirectly_ChangesValue()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.IsDarkTheme = true;
        Assert.True(vm.IsDarkTheme);

        vm.IsDarkTheme = false;
        Assert.False(vm.IsDarkTheme);
    }

    // ----------------------------------------------------------
    // AppStatus property
    // ----------------------------------------------------------

    [Fact]
    public void AppStatus_SetValue_ReflectsChange()
    {
        var vm = new MainViewModel();
        vm.AppStatus = "Processing...";

        Assert.Equal("Processing...", vm.AppStatus);
    }

    // ----------------------------------------------------------
    // Tab Management — NewTabCommand
    // ----------------------------------------------------------

    [Fact]
    public void NewTabCommand_Execute_AddsTab()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.NewTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Tabs);
    }

    [Fact]
    public void NewTabCommand_Execute_SetsActiveTab()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.NewTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.ActiveTab);
        Assert.True(vm.HasActiveTab);
    }

    [Fact]
    public void NewTabCommand_ExecutedTwice_AddsTwoTabs()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.NewTabCommand.Execute().Subscribe();
        vm.NewTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Tabs.Count);
    }

    // ----------------------------------------------------------
    // CloseActiveTab
    // ----------------------------------------------------------

    [Fact]
    public void CloseActiveTab_WhenNoTabs_DoesNothing()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        // Should not throw
        vm.CloseActiveTab();

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.ActiveTab);
    }

    [Fact]
    public void CloseTabCommand_WithOneTab_LeavesNoTabs()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.NewTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        vm.CloseTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.ActiveTab);
        Assert.False(vm.HasActiveTab);
    }

    [Fact]
    public void CloseTabCommand_WithTwoTabs_SetsNextTabActive()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.NewTabCommand.Execute().Subscribe();
        vm.NewTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        var firstTab = vm.Tabs[0];
        var secondTab = vm.Tabs[1];
        // second tab is currently active (last added)
        vm.ActiveTab = firstTab;
        Dispatcher.UIThread.RunJobs();

        vm.CloseTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Tabs);
        Assert.Equal(secondTab, vm.ActiveTab);
    }

    // ----------------------------------------------------------
    // CloseTab (specific tab reference)
    // ----------------------------------------------------------

    [Fact]
    public void CloseTab_NonActiveTab_ActiveRemainsUnchanged()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.NewTabCommand.Execute().Subscribe();
        vm.NewTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        var active = vm.ActiveTab;
        var other  = vm.Tabs.First(t => t != active);

        vm.CloseTab(other!);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(active, vm.ActiveTab);
        Assert.Single(vm.Tabs);
    }

    [Fact]
    public void CloseTab_ActiveTab_SwitchesToRemainingTab()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        vm.NewTabCommand.Execute().Subscribe();
        vm.NewTabCommand.Execute().Subscribe();
        Dispatcher.UIThread.RunJobs();

        var active = vm.ActiveTab!;
        vm.CloseTab(active);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(active, vm.Tabs);
        Assert.Single(vm.Tabs);
    }

    // ----------------------------------------------------------
    // Session persistence
    // ----------------------------------------------------------

    [Fact]
    public void SaveSession_DoesNotThrow()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        var ex = Record.Exception(() => vm.SaveSession());
        Assert.Null(ex);
    }

    [Fact]
    public void RestoreSession_WhenNoOpenFiles_DoesNotThrow()
    {
        var vm = new MainViewModel();
        Dispatcher.UIThread.RunJobs();

        var ex = Record.Exception(() => vm.RestoreSession());
        Assert.Null(ex);
    }
}
