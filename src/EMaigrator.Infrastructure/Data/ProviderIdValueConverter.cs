using EMaigrator.Core.Model;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EMaigrator.Infrastructure.Data;

public sealed class ProviderIdValueConverter : ValueConverter<ProviderId, string>
{
    public ProviderIdValueConverter()
        : base(v => v.Value, v => new ProviderId(v))
    {
    }
}
