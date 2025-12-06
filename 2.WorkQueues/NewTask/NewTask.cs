using RabbitMQ.Client;
using System.Text;

var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();

await channel.QueueDeclareAsync(
    queue: "task_queue", //jezeli wczesniej bylo durable false na danym queue,
    durable: true,       //to trzeba utworzyc nowe bo nie da się nadpisac typu
    exclusive: false,
    autoDelete: false,
    arguments: null);

var message = GetMessage(args);
var body = Encoding.UTF8.GetBytes(message);
var properties = new BasicProperties
{
    Persistent = true
};

await channel.BasicPublishAsync(
    exchange: string.Empty,
    routingKey: "task_queue",
    mandatory: true,
    basicProperties: properties,
    body: body);

Console.WriteLine($"[x] Sent {message}");

Console.WriteLine("Press [enter] to exit.");
Console.ReadLine();

static string GetMessage(string[] args)
{
    return ((args.Length > 0) ? string.Join(" ", args) : "Hello World!");
}