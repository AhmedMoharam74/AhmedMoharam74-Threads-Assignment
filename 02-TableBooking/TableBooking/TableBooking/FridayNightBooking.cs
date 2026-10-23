namespace TableBooking;

public sealed class FridayNightBooking
{
    readonly object _sync = new();

    public int TablesLeft { get; private set; } = 5;
    public int Booked { get; private set; }

    public void Book(string customerName)
    {
        lock (_sync)
        {
            if (TablesLeft <= 0)
                return;

            Thread.Sleep(20);
            TablesLeft--;
            Booked++;
            Console.WriteLine($"{customerName} booked a table. Left={TablesLeft}");
        }
    }
}
