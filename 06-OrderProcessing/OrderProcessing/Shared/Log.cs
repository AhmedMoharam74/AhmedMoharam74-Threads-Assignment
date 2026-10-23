namespace OrderProcessing.Shared;

public static class Log
{
    private static readonly object Gate = new();
    public static void Info(string message) => Write(message, ConsoleColor.Gray);
    public static void Success(string message) => Write(message, ConsoleColor.Green);
    public static void Error(string message) => Write(message, ConsoleColor.Red);
    private static void Write(string message, ConsoleColor color)
    {
        lock (Gate)
        {
            ConsoleColor old = Console.ForegroundColor;
            try { Console.ForegroundColor = color; Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Thread {Environment.CurrentManagedThreadId}] {message}"); }
            finally { Console.ForegroundColor = old; }
        }
    }
}
