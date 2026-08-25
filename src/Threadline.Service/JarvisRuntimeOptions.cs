namespace Threadline.Service;

public sealed record JarvisRuntimeOptions(
    bool Enabled,
    Uri BaseAddress,
    TimeSpan RequestTimeout,
    bool AllowRemoteRuntime)
{
    public const string PinnedUpstreamCommit = "b85535a50a3cece8cd269325b6b3ea2cb900e43f";
    public const string UpstreamRepository = "https://github.com/PersonalJarvis/PersonalJarvis";

    public static JarvisRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration.GetValue("Threadline:Jarvis:Enabled", true);
        var allowRemoteRuntime = configuration.GetValue("Threadline:Jarvis:AllowRemoteRuntime", false);
        var baseAddressText = configuration["Threadline:Jarvis:BaseAddress"];
        if (!Uri.TryCreate(baseAddressText, UriKind.Absolute, out var baseAddress))
        {
            baseAddress = new Uri("http://127.0.0.1:47821/", UriKind.Absolute);
        }

        if (baseAddress.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Threadline:Jarvis:BaseAddress must use http or https.");
        }

        if (!baseAddress.IsLoopback && !allowRemoteRuntime)
        {
            throw new InvalidOperationException(
                "Threadline:Jarvis:BaseAddress must be loopback unless Threadline:Jarvis:AllowRemoteRuntime is explicitly true.");
        }

        if (!baseAddress.AbsoluteUri.EndsWith('/', StringComparison.Ordinal))
        {
            baseAddress = new Uri(baseAddress.AbsoluteUri + "/", UriKind.Absolute);
        }

        var timeoutSeconds = Math.Clamp(
            configuration.GetValue("Threadline:Jarvis:RequestTimeoutSeconds", 30),
            2,
            300);

        return new JarvisRuntimeOptions(
            enabled,
            baseAddress,
            TimeSpan.FromSeconds(timeoutSeconds),
            allowRemoteRuntime);
    }
}
