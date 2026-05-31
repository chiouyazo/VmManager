using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VmManager.Agent.Services.Rdp;

public sealed class CertificateFactory
{
    private readonly X509Certificate2 _certificate;

    public CertificateFactory()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new CertificateRequest(
            "CN=VmManager RDP Proxy",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        X509Certificate2 generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(5)
        );

        _certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx));
    }

    public X509Certificate2 GetCertificate()
    {
        return new X509Certificate2(_certificate.Export(X509ContentType.Pfx));
    }
}
