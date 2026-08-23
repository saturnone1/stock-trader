module StockTrader.OptimizationWorker.MutualTlsHttpClient

open System
open System.IO
open System.Net.Http
open System.Net.Security
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open Microsoft.Extensions.Configuration

let private required (configuration: IConfiguration) name =
    match configuration[name] |> Option.ofObj with
    | Some value when not (String.IsNullOrWhiteSpace(value)) -> value
    | _ -> raise (InvalidOperationException($"Missing required TLS setting: {name}"))

let private roots path =
    let certificates = X509Certificate2Collection()
    certificates.ImportFromPemFile(path)
    if certificates.Count = 0 then
        raise (InvalidDataException($"No CA certificate found in {path}"))
    certificates

let private validateServer (trustedRoots: X509Certificate2Collection)
                           (_: obj) (certificate: X509Certificate | null)
                           (_: X509Chain | null) (errors: SslPolicyErrors) =
    match certificate with
    | null -> false
    | certificate when errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
                       || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) -> false
    | certificate ->
        use leaf = new X509Certificate2(certificate)
        use chain = new X509Chain()
        chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
        chain.ChainPolicy.CustomTrustStore.AddRange(trustedRoots)
        chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
        chain.ChainPolicy.VerificationFlags <- X509VerificationFlags.NoFlag
        chain.ChainPolicy.ApplicationPolicy.Add(Oid("1.3.6.1.5.5.7.3.1")) |> ignore
        chain.Build(leaf)

let create (configuration: IConfiguration) =
    let baseUrl = configuration["STOCKTRADER_CONTROL_API_URL"] |> Option.ofObj
    if baseUrl |> Option.exists (fun value -> value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) then
        let certificatePath = required configuration "STOCKTRADER_WORKER_CLIENT_CERT_PATH"
        let keyPath = required configuration "STOCKTRADER_WORKER_CLIENT_KEY_PATH"
        let authorityPath = required configuration "STOCKTRADER_WORKER_SERVER_CA_PATH"
        let certificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath)
        let handler = new SocketsHttpHandler()
        let clientCertificates = X509CertificateCollection()
        clientCertificates.Add(certificate) |> ignore
        handler.SslOptions.ClientCertificates <- clientCertificates
        let trustedRoots = roots authorityPath
        handler.SslOptions.RemoteCertificateValidationCallback <-
            RemoteCertificateValidationCallback(validateServer trustedRoots)
        new HttpClient(handler, true)
    else
        new HttpClient()
