using ArtigoTech.MicrosoftExtensionsResilience.Interfaces;
using ArtigoTech.MicrosoftExtensionsResilience.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Adiciona o HttpClient ao contêiner de serviços
        services.AddHttpClient();

        // Registrando a interface e sua implementação no contêiner de serviços
        services.AddScoped<IPostService, PostService>();
    })
    .Build();

using (var scope = host.Services.CreateScope())
{
    var postService = scope.ServiceProvider.GetRequiredService<IPostService>();
    await postService.ProcessarPostsAsync();
}

Console.WriteLine("Pressione qualquer tecla para sair...");
Console.ReadKey();
