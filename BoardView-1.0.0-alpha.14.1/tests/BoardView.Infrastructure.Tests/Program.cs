using BoardView.Application;
using BoardView.Configuration;
using BoardView.Contracts;
using BoardView.Core.Contracts;
using BoardView.Plugins;
using BoardView.Core.Repair;
using BoardView.Infrastructure.Repair;

namespace BoardView.Infrastructure.Tests;

/// <summary>Pruebas de infraestructura ejecutables sin dependencias de un framework externo.</summary>
internal static class Program
{
    private static int Main()
    {
        List<Action> tests =
        [
            ApplicationPathsCreateRequiredDirectories,
            PluginCatalogReturnsOnlyDllCandidates,
            OperationResultPreservesFailureInformation,
            ApplicationVersionHasExpectedValue,
            RepairWorkspaceRoundTrips
        ];

        int failures = 0;
        foreach (Action test in tests)
        {
            try
            {
                test();
                Console.WriteLine($"[OK] {test.Method.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"[ERROR] {test.Method.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"Pruebas ejecutadas: {tests.Count}. Fallos: {failures}.");
        return failures == 0 ? 0 : 1;
    }

    private static void ApplicationPathsCreateRequiredDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), $"BoardView.Tests.{Guid.NewGuid():N}");
        try
        {
            IApplicationPathProvider provider = new ApplicationPathProvider(root);
            provider.EnsureDirectoriesExist();
            Assert(Directory.Exists(provider.ApplicationDataDirectory), "No se creó el directorio de datos.");
            Assert(Directory.Exists(provider.LogDirectory), "No se creó el directorio de logs.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static void PluginCatalogReturnsOnlyDllCandidates()
    {
        string root = Path.Combine(Path.GetTempPath(), $"BoardView.Plugins.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Valid.Plugin.dll"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Ignored.txt"), string.Empty);
            IPluginCatalog catalog = new DirectoryPluginCatalog();
            IReadOnlyList<BoardView.Core.Plugins.PluginDescriptor> plugins = catalog.Discover(root);
            Assert(plugins.Count == 1, "El catálogo no filtró correctamente las extensiones.");
            Assert(plugins[0].Name == "Valid.Plugin", "El nombre del plugin no fue normalizado.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void OperationResultPreservesFailureInformation()
    {
        OperationResult result = OperationResult.Failure("TEST_ERROR", "Fallo controlado.");
        Assert(!result.IsSuccessful, "Un fallo no puede marcarse como satisfactorio.");
        Assert(result.ErrorCode == "TEST_ERROR", "Se perdió el código de error.");
        Assert(result.ErrorMessage == "Fallo controlado.", "Se perdió el mensaje de error.");
    }

    private static void ApplicationVersionHasExpectedValue() =>
        Assert(ApplicationInformation.Version == "1.0.0-alpha.1", "La versión central no coincide con la entrega.");


    private static void RepairWorkspaceRoundTrips()
    {
        string root = Path.Combine(Path.GetTempPath(), $"BoardView.Repair.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "session.bvrepair");
            IRepairWorkspaceStore store = new JsonRepairWorkspaceStore();
            RepairWorkspaceProject expected = new()
            {
                Name = "Prueba",
                BoardFilePath = "board.pdf",
                SchematicFilePath = "schematic.pdf",
                Annotations = [new RepairAnnotation { Reference = "U100", Notes = "Medir pin 1", Status = RepairStatus.Suspect }],
            };
            store.Save(path, expected);
            RepairWorkspaceProject actual = store.Load(path);
            Assert(actual.Name == expected.Name, "Se perdió el nombre del proyecto.");
            Assert(actual.Annotations.Count == 1 && actual.Annotations[0].Reference == "U100", "No se restauraron las notas.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
