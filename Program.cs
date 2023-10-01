using Briscola_Back_End.Controllers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

uint minutes = 5;
var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
Thread childThread = new(async () =>
{
    while (await timer.WaitForNextTickAsync())
    {
        // check game id state
        GameGenerationController.CheckGameIdStatus(minutes);
    }
});

childThread.Start();

app.UseHttpsRedirection();

app.UseWebSockets();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.Run();