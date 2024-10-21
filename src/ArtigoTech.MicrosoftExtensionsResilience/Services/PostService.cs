using ArtigoTech.MicrosoftExtensionsResilience.Dtos.Responses;
using ArtigoTech.MicrosoftExtensionsResilience.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Json;

namespace ArtigoTech.MicrosoftExtensionsResilience.Services
{
    public class PostService : IPostService
    {
        private readonly ILogger<PostService> _logger;
        private readonly HttpClient _httpClient;
        private readonly AsyncPolicy _retryPolicy;
        private readonly string _connectionString;

        public PostService(ILogger<PostService> logger, HttpClient httpClient) 
        { 
            _logger = logger;
            _httpClient = httpClient;

            var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var databasePath = Path.Combine(projectDirectory, "ResilienciaDB.db");
            _connectionString = $"Data Source={databasePath};Cache=Shared";

            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(5), 
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning("Tentativa {numeroTentativa} falhou! - Mensagem Erro: {mensagemErro} - Tentando novamente em {timeSpan}.", 
                            retryCount,
                            exception.Message, 
                            timeSpan);
                    });
        }
        
        public async Task ProcessarPostsAsync()
        {
            var idsPostsParaProcessamento = await ObterIdsPostsParaProcessamentoAsync();

            foreach (var idPost in idsPostsParaProcessamento) 
            {
                try
                {
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        var response = await _httpClient.GetAsync($"https://jsonplaceholder.typicode.com/posts/{idPost}");

                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("Falha ao consultar a API para o post de id: {id}. Status Code: {StatusCode}.", idPost, response.StatusCode);
                            throw new HttpRequestException($"Falha ao consultar a API para o post de id: {idPost}. Status Code: {response.StatusCode}.");
                        }

                        var postResponseDto = await response.Content.ReadFromJsonAsync<PostResponseDto>();

                        await AtualizarPostAsync(idPost, postResponseDto);
                        _logger.LogInformation("Post de id: {id}, processado com sucesso!", idPost);

                        return response;
                    });
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Falha ao processar o post de id: {id}. Salvando para reprocessamento.", idPost);
                    await SetarErroPostProcessamentoAsync(idPost, exception.Message);
                    await SalvarIdPostParaReprocessamentoAsync(idPost);
                }
            }
        }

        #region Métodos Privados

        private async Task<List<int>> ObterIdsPostsParaProcessamentoAsync()
        {
            var idsPosts = new List<int>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText =
                @"
                    SELECT 
                        Id, 
                        Titulo, 
                        Descricao, 
                        Processado, 
                        DataProcessamento,
                        Erro,    
                        UsuarioId 
                    FROM 
                        TB_Posts 
                    WHERE 
                        Processado = 0 
                        AND (Erro IS NULL OR Erro = '');
                ";

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        idsPosts.Add(reader.GetInt32(0));
                    }
                }
            }

            return idsPosts;
        }

        private async Task AtualizarPostAsync(int idPost, PostResponseDto postDto)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
            @"
                UPDATE TB_Posts
                SET 
                    Titulo = $tituloPost, 
                    Descricao = $descricaoPost, 
                    Processado = $processado, 
                    DataProcessamento = $dataProcessamento,
                    Usuarioid = $usuarioId
                WHERE Id = $idPost;
            ";

            command.Parameters.AddWithValue("$tituloPost", postDto.Title);
            command.Parameters.AddWithValue("$descricaoPost", postDto.Body);
            command.Parameters.AddWithValue("$processado", true);
            command.Parameters.AddWithValue("$dataProcessamento", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("$usuarioId", postDto.UserId);
            command.Parameters.AddWithValue("$idPost", idPost);

            await command.ExecuteNonQueryAsync();
        }

        public async Task SetarErroPostProcessamentoAsync(int idPost, string mensagemErro)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
            @"
                UPDATE TB_Posts
                SET
                    Processado = $processado,
                    DataProcessamento = $dataProcessamento,
                    Erro = $mensagemErro
                WHERE Id = $idPost;
            ";

            command.Parameters.AddWithValue("$processado", true);
            command.Parameters.AddWithValue("$dataProcessamento", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("$mensagemErro", mensagemErro);
            command.Parameters.AddWithValue("$idPost", idPost);

            await command.ExecuteNonQueryAsync();
        }

        private async Task SalvarIdPostParaReprocessamentoAsync(int idPostFalhaNoPrcessamento)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
            @"
                INSERT INTO TB_ReprocessamentoPosts (
                    IdPostFalhaProcessamento,
                    DataTentativaProcessamento,
                    Reprocessado
                ) 
                VALUES (
                    $idPostFalhaProcessamento, 
                    $dataTentativaProcessamento, 
                    $reprocessado
                );
            ";

            command.Parameters.AddWithValue("$idPostFalhaProcessamento", idPostFalhaNoPrcessamento);
            command.Parameters.AddWithValue("$dataTentativaProcessamento", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("$reprocessado", false);

            await command.ExecuteNonQueryAsync();
        }

        #endregion Métodos Privados
    }
}
