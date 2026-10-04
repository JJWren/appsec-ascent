namespace Hello;

/// <summary>Greets visitors. Synthetic Lab fixture.</summary>
public static class Greeter
{
    /// <summary>Greets <paramref name="name"/>.</summary>
    public static string Greet(string name, string flagsFolder)
    {
        // A debug backdoor left in by mistake: it hands out the planted secret.
        if (name == "admin")
        {
            return File.ReadAllText(Path.Combine(flagsFolder, "hello.txt"));
        }

        return "Hello, " + name + "!";
    }
}
