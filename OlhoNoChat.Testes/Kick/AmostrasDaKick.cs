using System.Text.Json;

namespace OlhoNoChat.Testes.Kick;

/// <summary>
/// Respostas e quadros no formato que a Kick mandou de verdade (2026-11-05, pela API do canal e pelo Centrifugo), com
/// nomes genéricos (viewer1…6) e ids inventados.
/// </summary>
internal static class AmostrasDaKick
{
    public const long IdDoCanal = 67262278;
    public const long IdDaSala = 66973867;

    /// <summary>O canal na API do site (só os campos que importam, com duas insígnias de assinante fora de ordem).</summary>
    public const string Canal = """
        {"id":67262278,"user_id":67575473,"slug":"seucanal","chatroom":{"id":66973867,"chatable_type":"App\\Models\\Channel"},
         "livestream":null,
         "subscriber_badges":[
           {"id":553611,"channel_id":67262278,"months":2,"badge_image":{"srcset":"","src":"https://files.kick.com/channel_subscriber_badges/553611/conversion/original-fullsize.png"}},
           {"id":553609,"channel_id":67262278,"months":1,"badge_image":{"srcset":"","src":"https://files.kick.com/channel_subscriber_badges/553609/conversion/original-fullsize.png"}},
           {"id":553620,"channel_id":67262278,"months":12,"badge_image":{"srcset":"","src":"https://files.kick.com/channel_subscriber_badges/553620/conversion/original-fullsize.png"}}
         ]}
        """;

    public const string Token = """{"data":{"token":"eyJhbGciOi.teste.assinatura"},"message":"success"}""";

    public const string Endereco = """{"data":{"connections":[{"credentials":{"url":"wss://realtime.us-east-1.platform.kick.com/connection/websocket"},"provider":"centrifugo"}],"mode":"websocket"},"message":"success"}""";

    public const string Conectou = """{"id":1,"connect":{"client":"437ec73e-d557-4162-a5e1-88c4c575d229","version":"6.9.7 PRO","expires":true,"ttl":2699,"ping":25,"pong":true}}""";

    public const string Assinou = """{"id":2,"subscribe":{}}""";

    /// <summary>O <c>data</c> de uma mensagem: assinante de 13 meses, presenteador, com o selo de nível e um selo novo escolhidos.</summary>
    public static string DadosDaMensagem(string id = "3873ad9f-1750-4649-95b9-9f8feef61198", string nome = "Viewer1",
        string texto = "boa [emote:37226:KEKW] demais", string selos = SelosDeAssinante, string selosNovos = SelosNovosEscolhidos) => $$"""
        {"id":"{{id}}","chatroom_id":66973867,"content":{{JsonSerializer.Serialize(texto)}},"type":"message","created_at":"2026-11-05T18:13:31+00:00",
         "sender":{"id":69923181,"username":"{{nome}}","slug":"{{nome.ToLowerInvariant()}}","identity":{"color":"#1475E1",
           "badges":{{selos}},"badges_v2":{{selosNovos}} } },
         "metadata":{"message_ref":"1793902410600"} }
        """;

    public const string SelosDeAssinante =
        """[{"type":"subscriber","text":"Subscriber","count":13,"sort_order":9},{"type":"sub_gifter","text":"Sub Gifter","count":1,"sort_order":8}]""";

    // Na ordem que a Kick mandou: um selo novo, o de nível (sort_order 1) e dois não escolhidos
    public const string SelosNovosEscolhidos = """
        [{"name":"GoldenK","badge_type":"global","image_url":"https:\/\/ext.cdn.kick.com\/chat\/badges\/GoldenK_019f591b.png","selected":true,"sort_order":4},
         {"name":"level","badge_type":"global","image_url":"https:\/\/ext.cdn.kick.com\/chat\/badges\/27_16ee146e.png","metadata":{"level":27},"selected":true,"sort_order":1},
         {"name":"Ghost","badge_type":"global","image_url":"https:\/\/ext.cdn.kick.com\/chat\/badges\/Ghost_b3f35326.png","selected":false,"sort_order":4},
         {"name":"WorldofWarcraftForeverBeta","badge_type":"global","image_url":"https:\/\/ext.cdn.kick.com\/chat\/badges\/WoW_c4ae6085.png","selected":true,"sort_order":4}]
        """;

    /// <summary>Um evento da sala como chega no Centrifugo: o <c>data</c> de dentro é JSON em texto.</summary>
    public static string Push(string evento, string dados) =>
        JsonSerializer.Serialize(new
        {
            push = new
            {
                channel = $"chatrooms.{IdDaSala}.v2",
                pub = new { data = new { @event = evento, data = dados }, tags = new { @event = evento } },
            },
        });

    public static string Mensagem(string id = "m1", string nome = "Viewer1", string texto = "oi") =>
        Push(@"App\Events\ChatMessageEvent", DadosDaMensagem(id, nome, texto, "[]", "[]"));
}
