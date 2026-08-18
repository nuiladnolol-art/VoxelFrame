using System;
using Raylib_cs;
class Program {
    static void Main() {
        foreach (var m in typeof(Raylib).GetMethods()) {
            if (m.Name == "SetShaderValue") {
                Console.WriteLine(m.ToString());
            }
        }
    }
}
