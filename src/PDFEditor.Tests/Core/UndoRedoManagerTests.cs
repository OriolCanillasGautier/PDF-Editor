using PDFEditor.Core.Services;
using Xunit;

namespace PDFEditor.Tests.Core;

/// <summary>
/// Tests for UndoRedoManager (undo, redo, history tracking)
/// </summary>
public class UndoRedoManagerTests
{
    [Fact]
    public void Initial_State_HasNoUndoRedo()
    {
        var mgr = new UndoRedoManager();
        Assert.False(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
        Assert.Equal(0, mgr.UndoCount);
        Assert.Equal(0, mgr.RedoCount);
    }

    [Fact]
    public void RecordAction_CanUndo_IsTrue()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Test", new byte[] { 1 }, new byte[] { 2 });
        Assert.True(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
        Assert.Equal(1, mgr.UndoCount);
    }

    [Fact]
    public void Undo_ReturnsStateBefore()
    {
        var mgr = new UndoRedoManager();
        var before = new byte[] { 1, 2, 3 };
        var after = new byte[] { 4, 5, 6 };
        mgr.RecordAction("Op1", before, after);

        var result = mgr.Undo();
        Assert.NotNull(result);
        Assert.Equal(before, result);
    }

    [Fact]
    public void Undo_ThenCanRedo_IsTrue()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Op1", new byte[] { 1 }, new byte[] { 2 });
        mgr.Undo();
        Assert.True(mgr.CanRedo);
        Assert.False(mgr.CanUndo);
    }

    [Fact]
    public void Redo_ReturnsStateAfter()
    {
        var mgr = new UndoRedoManager();
        var before = new byte[] { 1 };
        var after = new byte[] { 2 };
        mgr.RecordAction("Op1", before, after);
        mgr.Undo();

        var result = mgr.Redo();
        Assert.NotNull(result);
        Assert.Equal(after, result);
    }

    [Fact]
    public void Redo_AfterNewAction_IsCleared()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Op1", new byte[] { 1 }, new byte[] { 2 });
        mgr.Undo();
        Assert.True(mgr.CanRedo);

        mgr.RecordAction("Op2", new byte[] { 3 }, new byte[] { 4 });
        Assert.False(mgr.CanRedo);
    }

    [Fact]
    public void MultipleUndos_TraverseHistory()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Op1", new byte[] { 1 }, new byte[] { 2 });
        mgr.RecordAction("Op2", new byte[] { 2 }, new byte[] { 3 });
        mgr.RecordAction("Op3", new byte[] { 3 }, new byte[] { 4 });

        Assert.Equal(3, mgr.UndoCount);

        var r1 = mgr.Undo();
        Assert.Equal(new byte[] { 3 }, r1);
        var r2 = mgr.Undo();
        Assert.Equal(new byte[] { 2 }, r2);
        var r3 = mgr.Undo();
        Assert.Equal(new byte[] { 1 }, r3);

        Assert.False(mgr.CanUndo);
        Assert.Equal(3, mgr.RedoCount);
    }

    [Fact]
    public void UndoTo_JumpsMultipleSteps()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Op1", new byte[] { 1 }, new byte[] { 2 });
        mgr.RecordAction("Op2", new byte[] { 2 }, new byte[] { 3 });
        mgr.RecordAction("Op3", new byte[] { 3 }, new byte[] { 4 });

        var result = mgr.UndoTo(2);
        Assert.NotNull(result);
        Assert.Equal(new byte[] { 2 }, result);
        Assert.Equal(1, mgr.UndoCount);
        Assert.Equal(2, mgr.RedoCount);
    }

    [Fact]
    public void Clear_ResetsAllHistory()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Op1", new byte[] { 1 }, new byte[] { 2 });
        mgr.RecordAction("Op2", new byte[] { 2 }, new byte[] { 3 });
        mgr.Undo();

        mgr.Clear();
        Assert.False(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
        Assert.Equal(0, mgr.UndoCount);
        Assert.Equal(0, mgr.RedoCount);
    }

    [Fact]
    public void UndoDescription_ReturnsLatestDescription()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Rotate page", new byte[] { 1 }, new byte[] { 2 });
        Assert.Equal("Rotate page", mgr.UndoDescription);
    }

    [Fact]
    public void RedoDescription_ReturnsLatestUndone()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Delete page", new byte[] { 1 }, new byte[] { 2 });
        mgr.Undo();
        Assert.Equal("Delete page", mgr.RedoDescription);
    }

    [Fact]
    public void UndoHistoryDescriptions_ReturnsAllInOrder()
    {
        var mgr = new UndoRedoManager();
        mgr.RecordAction("Op1", new byte[] { 1 }, new byte[] { 2 });
        mgr.RecordAction("Op2", new byte[] { 2 }, new byte[] { 3 });
        mgr.RecordAction("Op3", new byte[] { 3 }, new byte[] { 4 });

        var history = mgr.UndoHistoryDescriptions;
        Assert.Equal(3, history.Count);
        Assert.Equal("Op3", history[0]); // Stack order: most recent first
        Assert.Equal("Op2", history[1]);
        Assert.Equal("Op1", history[2]);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsNull()
    {
        var mgr = new UndoRedoManager();
        Assert.Null(mgr.Undo());
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsNull()
    {
        var mgr = new UndoRedoManager();
        Assert.Null(mgr.Redo());
    }
}
