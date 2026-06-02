namespace EMaigrator.Connectors.Gmail.Tests;

/// <summary>
/// Fake service-account JSON for offline tests. The private key is a throwaway RSA key
/// generated solely for unit testing (no access to any real Google project). Regenerate with:
///   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out test.pem
/// then paste PEM with literal \n line breaks below.
/// </summary>
public static class TestServiceAccount
{
    // Throwaway 2048-bit RSA PKCS#8 key generated solely for offline unit tests (no real Google
    // project). It is a non-secret test fixture; it never authenticates against any live tenant.
    private const string PrivateKeyPem =
        "-----BEGIN PRIVATE KEY-----\\nMIIEugIBADANBgkqhkiG9w0BAQEFAASCBKQwggSgAgEAAoIBAQCPNV37uQ7cHZQc\\nYBE0nXdh+f/Cw2g7Q4NUZIh1sOXCWIHk3ScKYZRYmu4gATw5P2J2QLRSaFuzrLC+\\nlyLNLmWjwFhivZ5sjoHOAw+88BoyA7+kCMSSIYvSijshJvdNUvBO99YnTKR8Hogc\\nIoG4xvv0BVdXW37+KRTss+KNERXgWstVHchD7LEPkmSdn4Uvk5vQPPbtPk+RXGk0\\n23zp5E8KTfEjeg4UNDANJsONsYyRg2/N37b7bN9IRxLC4xi3xjYwn1ibouj1DAos\\nGOweLPY7kB6sTkX6m6Q7QILpFVb0Rv7HsGMtR2p31ITpGEjH2LlOq8RJxdfeYSfE\\nIArt3I7RAgMBAAECgf92Xwuq4JwtH9snroCKQl5GLflUvhA5wY7xrZIzNbUJn1Q+\\nyE4CLAYNTIKSZx2lyYP+xXJHa4Xg+K0J3K3My2etTXUWqNrPquYVqDzUqeH9Kqa3\\n/5ymQp7rDYyH1Ug03IlQZ1WBxmgZ8A1cCWm67JRLGjRyO2liIV2aXwC0JEXg20RR\\nShtFHlU+CiIkymZiUAbQ7QV2BNtvT8mA5cbpApTD2FzcDiUdijgUSLZW8Rj+NHeV\\nIR5xsspIwPUOUQJNxhYMHC7T7NjtavpEyqo5bjl4Z/4V7pJykRaJ6gheNYJ8wiIq\\ngfo5vbnMhqh3e/WWZPW68ZCSuQSkSBeC5FKTp4ECgYEAw5i9ATQNPlhIgVkpTPk+\\n1vP2WwwPN3faEb3qV0vVdNXpW9r9OcKd/QhZwidknzwNHcBFQPdfUMx5/VSqHfKD\\ndq6OZDDgLgUq4YDcoUEZHcjJ9fNEXjQiJPQ0fSyD4TBzqObiVJNwGQmCAZUf9m3V\\ntiYR/YNdU6wtwdhSA9gUhoECgYEAu277rJnlhhcK7TP4JTLSmJji+Krez8TR8hCD\\n3+h3de/TWkjDZVbh/LxcJA6hSUHXDW1Jw+I/h8zhbxXtsQDavLM0PU6tpiurvWaH\\nzyQ9XlPQ91t/A+xQkqIBrYh0rGWqaKVg+cb4N9m9qHZ0KgnwM/rSB0cbQXFT6AYw\\nLCxeAFECgYAoHKqmFIaiwngcDqzpnDPG4UEkatS0C2AtQ0VLocGktDmnHMHRlpfP\\nzGab6ng4L5iBAW0yZYimiUh7K2G3woQzUpjg8yUGSwkANe0JJNCByyufxMPAjfBy\\no6IgCYECLW2Ktc60iYfzmn+O04Y6g0vQjv4hf08kWasIldQ79ZRAAQKBgFIW7nUO\\nxf6vUuLGkxS/qIqa0zVzqLg4jHbHEura5o8ppVhya9mTbtCBMp28JpluE6DWz6rS\\nCV8RtV4wrXSLWkGw/t0m+1i+4a3HHQ304kfQz8G2Oe/e7P77o158WBU1RaglXk6m\\n/QmA/NauYnwS9Dffz2LOmrpTxxrksu511AmxAoGAFKf9MNH1uEPECl9R2lwMPS+Q\\nIw7CQQF+DxbgHEf646J93oO+ptLYrYIcidiUDgTbg35dC6u+Kl3Rw4r5V6EmAptT\\nAagmnjhTpZSJGuY622KWcCAhb481TL1mpz+loH84jT8838R25EsrOvj5oHQGHgta\\nF1gOBMUDWvldK/WoaG0=\\n-----END PRIVATE KEY-----\\n";

    public static string Json => "{" +
        "\"type\":\"service_account\"," +
        "\"project_id\":\"test-project\"," +
        "\"private_key_id\":\"00000000000000000000000000000000deadbeef\"," +
        "\"private_key\":\"" + PrivateKeyPem + "\"," +
        "\"client_email\":\"emaigrator-test@test-project.iam.gserviceaccount.com\"," +
        "\"client_id\":\"123456789012345678901\"," +
        "\"auth_uri\":\"https://accounts.google.com/o/oauth2/auth\"," +
        "\"token_uri\":\"https://oauth2.googleapis.com/token\"," +
        "\"auth_provider_x509_cert_url\":\"https://www.googleapis.com/oauth2/v1/certs\"," +
        "\"client_x509_cert_url\":\"https://www.googleapis.com/robot/v1/metadata/x509/emaigrator-test%40test-project.iam.gserviceaccount.com\"" +
        "}";
}
