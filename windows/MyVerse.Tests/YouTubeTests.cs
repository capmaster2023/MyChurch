using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MyVerse;

namespace MyVerse.Tests;

/// <summary>
/// Siloé Stream 1.6.0 : le direct YouTube contre un faux serveur de Google (même adresses, mêmes réponses JSON) : connexion du compte
/// (PKCE, adresse locale), jetons, création du direct, flux réutilisable, liaison, directs prévus, erreurs dites simplement.
/// </summary>
public static class YouTubeTests
{
    /// <summary>Un faux Google : chaque requête est notée, la réponse vient du gestionnaire.</summary>
    sealed class FauxGoogle : IDisposable
    {
        readonly HttpListener ecoute = new();
        public readonly string Adresse;
        public readonly List<(string Methode, string Chemin, string Corps, string Auth)> Recu = new();
        public Func<string, string, string, Dictionary<string, string>, (int Statut, string Json)> Repondre = (_, _, _, _) => (404, "{}");
        public FauxGoogle()
        {
            var t = new TcpListener(IPAddress.Loopback, 0); t.Start();
            int port = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop();
            Adresse = $"http://127.0.0.1:{port}";
            ecoute.Prefixes.Add(Adresse + "/");
            ecoute.Start();
            _ = Task.Run(Boucle);
        }
        async Task Boucle()
        {
            while (ecoute.IsListening)
            {
                HttpListenerContext c;
                try { c = await ecoute.GetContextAsync(); } catch (Exception) { return; }
                try
                {
                    string corps;
                    using (var l = new StreamReader(c.Request.InputStream, Encoding.UTF8)) corps = await l.ReadToEndAsync();
                    var chemin = c.Request.Url!.AbsolutePath;
                    var q = YouTubeCompte.LireRequete(c.Request.Url.Query.TrimStart('?'));
                    lock (Recu) Recu.Add((c.Request.HttpMethod, chemin + c.Request.Url.Query, corps, c.Request.Headers["Authorization"] ?? ""));
                    var (statut, json) = Repondre(c.Request.HttpMethod, chemin, corps, q);
                    var octets = Encoding.UTF8.GetBytes(json);
                    c.Response.StatusCode = statut;
                    c.Response.ContentType = "application/json";
                    await c.Response.OutputStream.WriteAsync(octets);
                    c.Response.Close();
                }
                catch (Exception) { try { c.Response.Abort(); } catch (Exception) { } }
            }
        }
        public void Dispose() { try { ecoute.Stop(); ecoute.Close(); } catch (Exception) { } }
    }

    static YouTubeCompte Compte(FauxGoogle g, Coffre coffre) => new(coffre, () => ("client-123.apps.googleusercontent.com", "secret-xyz"))
    {
        AdresseAutorisation = g.Adresse + "/o/oauth2/v2/auth", AdresseJeton = g.Adresse + "/token", AdresseRevocation = g.Adresse + "/revoke",
        AdresseApi = g.Adresse + "/youtube/v3", AdresseEnvoi = g.Adresse + "/upload/youtube/v3",
    };

    static string Erreur(string raison, int code, string message = "erreur") =>
        new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message, ["errors"] = new JsonArray(new JsonObject { ["reason"] = raison }) } }.ToJsonString();

    public static async Task RunAsync(Action<bool, string, string?> ok)
    {
        string dossier = Path.Combine(Path.GetTempPath(), "siloestream-youtube-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dossier);

        // ------------------------------------------------------------ le navigateur revient sur l'adresse locale
        {
            var ecoute = new TcpListener(IPAddress.Loopback, 0); ecoute.Start();
            int port = ((IPEndPoint)ecoute.LocalEndpoint).Port;
            var attente = YouTubeCompte.AttendreCodeAsync(ecoute, "etat-1", CancellationToken.None);
            // une connexion ouverte d'avance par le navigateur, sans rien envoyer, puis l'icône, puis la vraie réponse
            using (var vide = new TcpClient()) { await vide.ConnectAsync(IPAddress.Loopback, port); }
            using var http = new HttpClient();
            using (var icone = await http.GetAsync($"http://127.0.0.1:{port}/favicon.ico")) ok(icone.StatusCode == HttpStatusCode.NotFound, "adresse locale : l'icône du navigateur est ignorée", null);
            using (var mauvais = await http.GetAsync($"http://127.0.0.1:{port}/?code=pirate&state=autre")) ok(mauvais.StatusCode == HttpStatusCode.BadRequest, "adresse locale : un autre « state » est refusé", null);
            var page = await http.GetStringAsync($"http://127.0.0.1:{port}/?state=etat-1&code=4%2F0Abc&scope=youtube");
            var code = await attente.WaitAsync(TimeSpan.FromSeconds(10));
            ok(code == "4/0Abc" && page.Contains("Siloé Stream est connecté à YouTube"), "adresse locale : le code de Google est reçu, la page dit de revenir à Siloé Stream", code);
            var refus = YouTubeCompte.AttendreCodeAsync(ecoute, "etat-2", CancellationToken.None);
            _ = http.GetStringAsync($"http://127.0.0.1:{port}/?error=access_denied&state=etat-2");
            try { await refus.WaitAsync(TimeSpan.FromSeconds(10)); ok(false, "connexion refusée dans Google : erreur claire", null); }
            catch (ErreurYouTube e) { ok(e.Raison == "access_denied" && e.Message.Contains("refusée"), "connexion refusée dans Google : erreur claire", e.Message); }
            ecoute.Stop();
        }

        // ------------------------------------------------------------ connexion complète (PKCE), puis l'API
        using var g = new FauxGoogle();
        var coffre = new Coffre(dossier);
        var compte = Compte(g, coffre);
        string? defi = null, retour = null, verificateurRecu = null;
        int jetonsDonnes = 0;
        string fluxCree = "";
        g.Repondre = (methode, chemin, corps, q) =>
        {
            var champs = YouTubeCompte.LireRequete(corps);
            switch (methode, chemin)
            {
                case ("POST", "/token") when champs.GetValueOrDefault("grant_type") == "authorization_code":
                    verificateurRecu = champs.GetValueOrDefault("code_verifier");
                    bool bon = champs.GetValueOrDefault("code") == "code-ok" && champs.GetValueOrDefault("client_secret") == "secret-xyz" && champs.GetValueOrDefault("redirect_uri") == retour
                        && verificateurRecu != null && YouTubeCompte.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificateurRecu))) == defi;
                    jetonsDonnes++;
                    return bon ? (200, "{\"access_token\":\"acces-1\",\"expires_in\":3599,\"refresh_token\":\"rafraichir-1\",\"token_type\":\"Bearer\"}") : (400, "{\"error\":\"invalid_grant\"}");
                case ("POST", "/token") when champs.GetValueOrDefault("grant_type") == "refresh_token":
                    jetonsDonnes++;
                    return champs.GetValueOrDefault("refresh_token") == "rafraichir-1" ? (200, "{\"access_token\":\"acces-2\",\"expires_in\":3599}") : (400, "{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}");
                case ("GET", "/youtube/v3/channels"):
                    return (200, "{\"items\":[{\"id\":\"UC123\",\"snippet\":{\"title\":\"PENTECÔTE MONTRÉAL\"}}]}");
                case ("POST", "/youtube/v3/liveBroadcasts"):
                    return (200, "{\"id\":\"direct-1\",\"status\":{\"lifeCycleStatus\":\"created\"}}");
                case ("PUT", "/youtube/v3/videos"):
                    return (200, "{\"id\":\"direct-1\"}");
                case ("POST", "/upload/youtube/v3/thumbnails/set"):
                    return (403, Erreur("forbidden", 403, "The authenticated user doesn't have permissions to upload and set custom video thumbnails."));
                case ("GET", "/youtube/v3/liveStreams"):
                    return q.GetValueOrDefault("id") == fluxCree && fluxCree.Length > 0
                        ? (200, "{\"items\":[{\"id\":\"" + fluxCree + "\",\"cdn\":{\"ingestionInfo\":{\"streamName\":\"abcd-efgh-ijkl-mnop\",\"ingestionAddress\":\"rtmp://a.rtmp.youtube.com/live2\",\"rtmpsIngestionAddress\":\"rtmps://a.rtmps.youtube.com/live2\"}},\"status\":{\"streamStatus\":\"active\",\"healthStatus\":{\"status\":\"good\"}}}]}")
                        : (200, "{\"items\":[]}");
                case ("POST", "/youtube/v3/liveStreams"):
                    fluxCree = "flux-9";
                    return (200, "{\"id\":\"flux-9\",\"cdn\":{\"ingestionInfo\":{\"streamName\":\"abcd-efgh-ijkl-mnop\",\"ingestionAddress\":\"rtmp://a.rtmp.youtube.com/live2\",\"rtmpsIngestionAddress\":\"rtmps://a.rtmps.youtube.com/live2\"}}}");
                case ("POST", "/youtube/v3/liveBroadcasts/bind"):
                    return (200, "{\"id\":\"direct-1\",\"contentDetails\":{\"boundStreamId\":\"flux-9\"}}");
                case ("POST", "/youtube/v3/liveBroadcasts/transition"):
                    return q.GetValueOrDefault("broadcastStatus") == "complete" ? (403, Erreur("redundantTransition", 403)) : (200, "{\"id\":\"direct-1\",\"status\":{\"lifeCycleStatus\":\"live\"}}");
                case ("GET", "/youtube/v3/liveBroadcasts"):
                    if (q.ContainsKey("id")) return (200, "{\"items\":[{\"id\":\"direct-1\",\"snippet\":{\"title\":\"Culte\"},\"status\":{\"lifeCycleStatus\":\"live\",\"privacyStatus\":\"public\"},\"contentDetails\":{\"boundStreamId\":\"flux-9\",\"enableAutoStart\":true}}]}");
                    return q.GetValueOrDefault("broadcastStatus") == "active"
                        ? (200, "{\"items\":[{\"id\":\"direct-0\",\"snippet\":{\"title\":\"Direct en cours\",\"scheduledStartTime\":\"2026-11-15T14:00:00Z\"},\"status\":{\"lifeCycleStatus\":\"live\",\"privacyStatus\":\"public\"},\"contentDetails\":{\"boundStreamId\":\"flux-1\"}}]}")
                        : (200, "{\"items\":[{\"id\":\"direct-3\",\"snippet\":{\"title\":\"Culte de dimanche\",\"scheduledStartTime\":\"2026-11-22T15:00:00Z\"},\"status\":{\"lifeCycleStatus\":\"ready\",\"privacyStatus\":\"unlisted\"},\"contentDetails\":{\"enableAutoStart\":false}}," +
                                 "{\"id\":\"direct-2\",\"snippet\":{\"title\":\"Veillée\",\"scheduledStartTime\":\"2026-11-20T23:00:00Z\"},\"status\":{\"lifeCycleStatus\":\"created\",\"privacyStatus\":\"public\"},\"contentDetails\":{}}]}");
            }
            return (404, Erreur("notFound", 404, "inconnu " + methode + " " + chemin));
        };

        ok(!compte.Connecte && compte.Disponible, "YouTube : pas encore connecté, identifiant Google présent", null);
        var chaine = await compte.ConnecterAsync(url =>
        {
            // le « navigateur » : la page de Google renvoie le code à Siloé Stream
            var q = YouTubeCompte.LireRequete(new Uri(url).Query.TrimStart('?'));
            defi = q.GetValueOrDefault("code_challenge"); retour = q.GetValueOrDefault("redirect_uri");
            bool bonneDemande = q.GetValueOrDefault("client_id") == "client-123.apps.googleusercontent.com" && q.GetValueOrDefault("code_challenge_method") == "S256"
                && q.GetValueOrDefault("scope") == YouTubeCompte.Portee && q.GetValueOrDefault("access_type") == "offline" && retour!.StartsWith("http://127.0.0.1:");
            ok(bonneDemande, "connexion : page de Google avec PKCE (S256), accès durable, retour sur 127.0.0.1", url.Length > 0 ? retour : null);
            _ = new HttpClient().GetStringAsync(retour + "?code=code-ok&state=" + Uri.EscapeDataString(q.GetValueOrDefault("state") ?? ""));
        }).WaitAsync(TimeSpan.FromSeconds(20));
        ok(chaine.Titre == "PENTECÔTE MONTRÉAL" && compte.Connecte && coffre.Lire(YouTubeCompte.CleJeton) == "rafraichir-1", "connexion : jetons échangés (vérificateur PKCE juste), chaîne lue, accès gardé", chaine.Titre);
        ok(new Coffre(dossier).Lire(YouTubeCompte.CleJeton) == "rafraichir-1", "le coffre garde l'accès après un redémarrage", null);
        ok(g.Recu.Any(r => r.Chemin.StartsWith("/youtube/v3/channels") && r.Auth == "Bearer acces-1"), "l'API reçoit le jeton d'accès", null);

        // ------------------------------------------------------------ créer le direct
        var m = new ModeleDirect { Titre = "Culte du dimanche 15 novembre", Description = "Bienvenue", Confidentialite = "unlisted", Categorie = "29", Enfants = false, Latence = "ultraLow", Dvr = true, Video360 = true, Miniature = "" };
        var id = await compte.CreerDirectAsync(m, new DateTimeOffset(2026, 11, 15, 15, 0, 0, TimeSpan.Zero));
        var demande = JsonNode.Parse(g.Recu.Last(r => r.Chemin.StartsWith("/youtube/v3/liveBroadcasts?part=")).Corps)!;
        ok(id == "direct-1" && (string?)demande["snippet"]?["title"] == m.Titre && (string?)demande["status"]?["privacyStatus"] == "unlisted"
            && (bool?)demande["status"]?["selfDeclaredMadeForKids"] == false && (string?)demande["snippet"]?["scheduledStartTime"] == "2026-11-15T15:00:00Z",
            "créer le direct : titre, confidentialité, enfants, heure (UTC)", demande.ToJsonString());
        ok((bool?)demande["contentDetails"]?["enableAutoStart"] == true && (bool?)demande["contentDetails"]?["enableAutoStop"] == false
            && (bool?)demande["contentDetails"]?["enableDvr"] == false && (string?)demande["contentDetails"]?["projection"] == "rectangular" && (string?)demande["contentDetails"]?["latencyPreference"] == "ultraLow",
            "très faible latence : sans DVR ni 360° ; démarre avec l'image, ne s'arrête pas sur une coupure", null);
        await compte.CategorieAsync(id, m);
        var cat = JsonNode.Parse(g.Recu.Last(r => r.Methode == "PUT").Corps)!;
        ok((string?)cat["id"] == "direct-1" && (string?)cat["snippet"]?["categoryId"] == "29" && (string?)cat["snippet"]?["title"] == m.Titre, "catégorie de la vidéo du direct", null);
        var miniature = Path.Combine(dossier, "miniature.jpg");
        File.WriteAllBytes(miniature, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        try { await compte.MiniatureAsync(id, miniature); ok(false, "miniature refusée (chaîne non vérifiée) : message clair", null); }
        catch (ErreurYouTube e) { ok(e.Message.Contains("vérifiée"), "miniature refusée (chaîne non vérifiée) : message clair", e.Message); }

        // ------------------------------------------------------------ flux réutilisable, liaison, transition
        var flux = await compte.FluxAsync(null);
        ok(flux is { Id: "flux-9", Cle: "abcd-efgh-ijkl-mnop", Adresse: "rtmps://a.rtmps.youtube.com/live2" }, "un flux réutilisable est créé (adresse RTMPS de YouTube)", flux.ToString());
        int creations = g.Recu.Count(r => r.Methode == "POST" && r.Chemin.StartsWith("/youtube/v3/liveStreams"));
        var memeFlux = await compte.FluxAsync("flux-9");
        ok(memeFlux == flux && g.Recu.Count(r => r.Methode == "POST" && r.Chemin.StartsWith("/youtube/v3/liveStreams")) == creations, "le flux gardé est repris (même clé à chaque direct)", null);
        ok(AdresseDirect.Pour(flux) == "rtmps://a.rtmps.youtube.com/live2/abcd-efgh-ijkl-mnop", "l'adresse complète du direct", null);
        await compte.LierAsync(id, flux.Id);
        ok(g.Recu.Any(r => r.Chemin.StartsWith("/youtube/v3/liveBroadcasts/bind") && r.Chemin.Contains("id=direct-1") && r.Chemin.Contains("streamId=flux-9")), "le direct est lié au flux", null);
        await compte.TransitionAsync(id, "live");
        await compte.TransitionAsync(id, "complete");
        ok(true, "transition déjà faite : pas une erreur", null);
        var etat = await compte.EtatFluxAsync("flux-9");
        ok(etat == ("active", "good"), "état du flux : YouTube reçoit l'image", etat.ToString());
        var d = await compte.DirectAsync("direct-1");
        ok(d is { Statut: "live", FluxLie: "flux-9", DemarrageAuto: true }, "état du direct", d?.ToString());

        // ------------------------------------------------------------ directs déjà prévus
        var prevus = await compte.DirectsAsync();
        ok(prevus.Select(p => p.Id).SequenceEqual(new[] { "direct-0", "direct-2", "direct-3" }) && prevus[2].Confidentialite == "unlisted" && prevus[1].Debut?.Day == 20,
            "directs prévus : le direct en cours d'abord, puis les plus proches", string.Join(",", prevus.Select(p => p.Id)));

        // ------------------------------------------------------------ jetons : rafraîchir, accès retiré
        var champJeton = typeof(YouTubeCompte).GetField("expiration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        champJeton.SetValue(compte, DateTimeOffset.UtcNow);
        int avant = jetonsDonnes;
        await compte.ChaineAsync();
        ok(jetonsDonnes == avant + 1 && g.Recu.Last().Auth == "Bearer acces-2", "jeton d'accès expiré : refait tout seul", null);
        coffre.Ecrire(YouTubeCompte.CleJeton, "rafraichir-retire");
        champJeton.SetValue(compte, DateTimeOffset.UtcNow);
        try { await compte.ChaineAsync(); ok(false, "accès retiré : le compte se déconnecte, message clair", null); }
        catch (ErreurYouTube e) { ok(e.Raison == "invalid_grant" && !compte.Connecte && e.Message.Contains("reconnectez"), "accès retiré : le compte se déconnecte, message clair", e.Message); }

        // ------------------------------------------------------------ erreurs dites simplement
        ok(YouTubeCompte.Erreur(JsonNode.Parse(Erreur("liveStreamingNotEnabled", 403)), "", 403).Message.Contains("Passer au direct"), "direct non activé sur la chaîne : quoi faire", null);
        ok(YouTubeCompte.Erreur(JsonNode.Parse(Erreur("quotaExceeded", 403)), "", 403).Message.Contains("clé de stream"), "quota de YouTube : utiliser la clé de stream", null);
        ok(YouTubeCompte.Erreur(null, "<html>panne</html>", 502).Message.StartsWith("YouTube : "), "réponse illisible : message brut court", null);

        // ------------------------------------------------------------ sans identifiant Google : la clé de stream
        var sansId = new YouTubeCompte(new Coffre(dossier), () => ("", ""));
        ok(!sansId.Disponible, "sans identifiant Google : « Connecter le compte » indisponible", null);
        try { await sansId.ConnecterAsync(_ => { }); ok(false, "sans identifiant : message qui renvoie à la clé de stream", null); }
        catch (ErreurYouTube e) { ok(e.Message.Contains("clé de stream"), "sans identifiant : message qui renvoie à la clé de stream", e.Message); }

        // ------------------------------------------------------------ clé de stream, autre service
        var r = new ReglagesDirect { Mode = ReglagesDirect.Cle };
        ok(AdresseDirect.Construire(r, coffre) == null, "clé de stream vide : pas d'adresse", null);
        coffre.Ecrire(AdresseDirect.CleStream, " xxxx-yyyy-zzzz-wwww ");
        ok(AdresseDirect.Construire(r, coffre) == "rtmps://a.rtmps.youtube.com:443/live2/xxxx-yyyy-zzzz-wwww", "clé de stream : RTMPS de YouTube", AdresseDirect.Construire(r, coffre));
        r.Mode = ReglagesDirect.Autre; r.Serveur = "rtmps://live-api-s.facebook.com:443/rtmp/";
        coffre.Ecrire(AdresseDirect.CleAutre, "FB-1234");
        ok(AdresseDirect.Construire(r, coffre) == "rtmps://live-api-s.facebook.com:443/rtmp/FB-1234", "autre service : adresse + clé", null);
        r.Serveur = "http://exemple.com";
        ok(AdresseDirect.Construire(r, coffre) == null, "autre service : seulement rtmp:// ou rtmps://", null);
        ok(AdresseDirect.Masquer("Erreur rtmps://a.rtmps.youtube.com/live2/xxxx-yyyy-zzzz-wwww : I/O error", "rtmps://a.rtmps.youtube.com:443/live2/xxxx-yyyy-zzzz-wwww") == "Erreur rtmps://a.rtmps.youtube.com/live2/•••• : I/O error",
            "la clé n'apparaît jamais dans un message", null);
        coffre.Ecrire(YouTubeCompte.CleJeton, "rafraichir-1");
        await compte.DeconnecterAsync();
        ok(!compte.Connecte && g.Recu.Any(x => x.Chemin.StartsWith("/revoke")), "déconnecter : l'accès est oublié et retiré chez Google", null);
        var rd = new ReglagesDirect { Mode = "n'importe", Qualite = "4k", Modele = null! };
        rd.Normaliser();
        ok(rd.Mode == ReglagesDirect.Compte && rd.Qualite == "720p" && rd.Modele.Confidentialite == "public" && rd.Taille == (1280, 720, 4500), "réglages du direct remis en ordre", null);
        var r4 = new ReglagesDirect { Qualite = "2160p" }; r4.Normaliser();
        ok(r4.Qualite == "2160p" && r4.EnQuatreK && r4.Taille == (3840, 2160, 30000) && !new ReglagesDirect { Qualite = "1080p" }.EnQuatreK, "direct en 4K : 3840 × 2160, 30 Mb/s", null);
        try { Directory.Delete(dossier, true); } catch (Exception) { }
    }
}
