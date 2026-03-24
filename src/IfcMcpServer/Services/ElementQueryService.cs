using System.Text.RegularExpressions;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Services;

public record PropertySetDefinition(string Name, IReadOnlyList<string> PropertyNames);
public record StoreyInfo(string Name, double Elevation, int ElementCount);
public record ClassificationInfo(string? SystemName, string Identification, string? Name);

public class ElementQueryService
{
    public IEnumerable<IIfcProduct> QueryElements(
        IModel model,
        string? ifcType,
        string? classification,
        string[]? propertyFilters)
    {
        IEnumerable<IIfcProduct> elements = model.Instances.OfType<IIfcProduct>();

        if (!string.IsNullOrEmpty(ifcType))
            elements = FilterByIfcType(elements, ifcType, model);

        if (!string.IsNullOrEmpty(classification))
            elements = FilterByClassification(elements, classification);

        if (propertyFilters is { Length: > 0 })
            elements = FilterByProperties(elements, propertyFilters);

        return elements;
    }

    private IEnumerable<IIfcProduct> FilterByIfcType(IEnumerable<IIfcProduct> elements, string ifcType, IModel model)
    {
        var expressType = model.Metadata.ExpressType(ifcType.ToUpperInvariant());
        if (expressType == null)
        {
            return elements.Where(e =>
                e.GetType().Name.Equals(ifcType, StringComparison.OrdinalIgnoreCase));
        }

        var validTypes = new HashSet<Type>(
            expressType.SubTypes?.Select(st => st.Type) ?? Enumerable.Empty<Type>())
        { expressType.Type };

        return elements.Where(e => validTypes.Any(t => t.IsInstanceOfType(e)));
    }

    private IEnumerable<IIfcProduct> FilterByClassification(IEnumerable<IIfcProduct> elements, string classification)
    {
        var pattern = "^" + Regex.Escape(classification).Replace("\\*", ".*") + "$";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        return elements.Where(e =>
        {
            var refs = GetClassificationReferences(e);
            return refs.Any(r =>
                (r.Identification != null && regex.IsMatch(r.Identification.ToString())) ||
                (r.Name != null && regex.IsMatch(r.Name.ToString())));
        });
    }

    private IEnumerable<IIfcProduct> FilterByProperties(IEnumerable<IIfcProduct> elements, string[] filters)
    {
        var parsed = filters.Select(ParsePropertyFilter).ToList();
        return elements.Where(e => parsed.All(f => MatchesPropertyFilter(e, f)));
    }

    public static IEnumerable<IIfcClassificationReference> GetClassificationReferences(IIfcProduct element)
    {
        var direct = element.HasAssociations
            .OfType<IIfcRelAssociatesClassification>()
            .Select(r => r.RelatingClassification)
            .OfType<IIfcClassificationReference>();

        var typeRefs = Enumerable.Empty<IIfcClassificationReference>();
        var typeDefs = element.IsTypedBy.FirstOrDefault()?.RelatingType;
        if (typeDefs != null)
        {
            typeRefs = typeDefs.HasAssociations
                .OfType<IIfcRelAssociatesClassification>()
                .Select(r => r.RelatingClassification)
                .OfType<IIfcClassificationReference>();
        }

        return direct.Concat(typeRefs);
    }

    public IEnumerable<ClassificationInfo> GetClassifications(IModel model)
    {
        return model.Instances.OfType<IIfcClassificationReference>()
            .Select(r =>
            {
                var system = r.ReferencedSource is IIfcClassification c ? c.Name.ToString() : null;
                return new ClassificationInfo(system, r.Identification.ToString() ?? "", r.Name.ToString());
            })
            .Distinct();
    }

    public IEnumerable<PropertySetDefinition> GetPropertySetDefinitions(IModel model, string? ifcType)
    {
        var elements = ifcType != null
            ? QueryElements(model, ifcType, null, null)
            : model.Instances.OfType<IIfcProduct>();

        var psetMap = new Dictionary<string, HashSet<string>>();

        foreach (var element in elements)
        {
            foreach (var rel in element.IsDefinedBy)
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet pset)
                {
                    var name = pset.Name?.ToString() ?? "(unnamed)";
                    if (!psetMap.ContainsKey(name))
                        psetMap[name] = new HashSet<string>();

                    foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                        psetMap[name].Add(prop.Name.ToString());
                }
            }
        }

        return psetMap.Select(kv => new PropertySetDefinition(kv.Key, kv.Value.OrderBy(n => n).ToList()));
    }

    public IEnumerable<StoreyInfo> GetStoreys(IModel model)
    {
        return model.Instances.OfType<IIfcBuildingStorey>()
            .OrderBy(s => s.Elevation?.Value ?? 0)
            .Select(s => new StoreyInfo(
                s.Name?.ToString() ?? "(unnamed)",
                (double)(s.Elevation ?? 0),
                s.ContainsElements.SelectMany(r => r.RelatedElements).Count()));
    }

    public static string? GetStoreyName(IIfcProduct element)
    {
        var storey = element.Model.Instances.OfType<IIfcRelContainedInSpatialStructure>()
            .Where(r => r.RelatedElements.Contains(element))
            .Select(r => r.RelatingStructure)
            .OfType<IIfcBuildingStorey>()
            .FirstOrDefault();
        return storey?.Name?.ToString();
    }

    private record PropertyFilter(string PsetName, string PropName, string Operator, string Value);

    private static PropertyFilter ParsePropertyFilter(string filter)
    {
        var operators = new[] { "!=", ">=", "<=", ">", "<", "=" };
        foreach (var op in operators)
        {
            var idx = filter.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0)
            {
                var left = filter[..idx];
                var value = filter[(idx + op.Length)..];
                var dotIdx = left.IndexOf('.');
                if (dotIdx < 0)
                    throw new ArgumentException($"Property filter must be in format 'PsetName.PropertyName{op}Value': {filter}");
                return new PropertyFilter(left[..dotIdx], left[(dotIdx + 1)..], op, value);
            }
        }
        throw new ArgumentException($"Property filter must contain an operator (=, !=, >, <, >=, <=): {filter}");
    }

    private static bool MatchesPropertyFilter(IIfcProduct element, PropertyFilter filter)
    {
        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcPropertySet pset &&
                string.Equals(pset.Name?.ToString(), filter.PsetName, StringComparison.OrdinalIgnoreCase))
            {
                var prop = pset.HasProperties
                    .OfType<IIfcPropertySingleValue>()
                    .FirstOrDefault(p => string.Equals(p.Name.ToString(), filter.PropName, StringComparison.OrdinalIgnoreCase));

                if (prop?.NominalValue == null) return false;

                var actual = prop.NominalValue.ToString() ?? "";
                return EvaluateOperator(actual, filter.Operator, filter.Value);
            }
        }
        return false;
    }

    private static bool EvaluateOperator(string actual, string op, string expected)
    {
        if (op == "=" || op == "!=")
        {
            var equals = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            return op == "=" ? equals : !equals;
        }

        if (!double.TryParse(actual, out var actualNum))
            throw new InvalidOperationException($"Operator '{op}' requires numeric values, but got '{actual}'");
        if (!double.TryParse(expected, out var expectedNum))
            throw new InvalidOperationException($"Operator '{op}' requires numeric values, but got '{expected}'");

        return op switch
        {
            ">" => actualNum > expectedNum,
            "<" => actualNum < expectedNum,
            ">=" => actualNum >= expectedNum,
            "<=" => actualNum <= expectedNum,
            _ => throw new ArgumentException($"Unknown operator: {op}")
        };
    }
}
