using System.Reflection;
using Raylib_cs;

var rlglType = typeof(Raylib).Assembly.GetType("Raylib_cs.Rlgl");
Console.WriteLine("=== Methods containing 'Shader' or 'shader' ===");
foreach (var m in typeof(Raylib).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.Name.Contains("Shader", StringComparison.OrdinalIgnoreCase)))
    Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");

Console.WriteLine("\n=== Methods containing 'Load' ===");
foreach (var m in typeof(Raylib).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.Name.Contains("Load", StringComparison.OrdinalIgnoreCase) && !m.Name.Contains("Shader", StringComparison.OrdinalIgnoreCase))
    .OrderBy(m => m.Name))
    Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");

Console.WriteLine("\n=== Shader struct fields ===");
foreach (var f in typeof(Shader).GetFields(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"  {f.FieldType.Name} {f.Name}");

Console.WriteLine("\n=== Mesh struct fields ===");
foreach (var f in typeof(Mesh).GetFields(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"  {f.FieldType.Name} {f.Name}");

Console.WriteLine("\n=== Material struct full ===");
foreach (var f in typeof(Material).GetFields(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"  {f.FieldType.Name} {f.Name}");
