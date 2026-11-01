using System.Drawing;
using KeyboardMouse.Core;

namespace KeyboardMouse.Tests;

public class MarksTests
{
    [Fact]
    public void Marks_SurviveAReload()
    {
        string path = NewPath();

        try
        {
            var marks = new Marks(path);
            marks.Set('A', new Point(10, 20));
            marks.Set('z', new Point(-40, 7));
            marks.Dispose();

            using var reloaded = new Marks(path);
            Assert.True(reloaded.TryGet('a', out var a));
            Assert.Equal(new Point(10, 20), a);
            Assert.True(reloaded.TryGet('Z', out var z));
            Assert.Equal(new Point(-40, 7), z);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void MissingFile_StartsEmpty()
    {
        string path = NewPath();

        try
        {
            using var marks = new Marks(path);
            Assert.Empty(marks.All);
            Assert.False(marks.TryGet('a', out _));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void CorruptFile_IsIgnoredInsteadOfCrashing()
    {
        string path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        try
        {
            using var marks = new Marks(path);
            Assert.Empty(marks.All);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void NamesOutsideAToZ_AreIgnored()
    {
        var marks = new InMemoryMarkStore();

        marks.Set('1', new Point(1, 2));
        marks.Set(';', new Point(3, 4));
        marks.Set('a', new Point(5, 6));

        Assert.Single(marks.All);
        Assert.True(marks.TryGet('A', out var a));
        Assert.Equal(new Point(5, 6), a);
        Assert.False(marks.TryGet('1', out _));
    }

    private static string NewPath() => Path.Combine(
        Path.GetTempPath(),
        "KeyboardMouse.Tests",
        Guid.NewGuid().ToString("N"),
        "marks.json");

    private static void Delete(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
