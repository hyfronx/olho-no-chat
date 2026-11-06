using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OlhoNoChat.Sistema;

/// <summary>
/// Pastas que só o usuário do Windows que usa o app abre: sem herdar as permissões da pasta de cima (que deixam os
/// Administradores e o SYSTEM entrarem) e só com a pessoa como dona, com acesso total, que passa para os arquivos de dentro.
/// </summary>
public static class PastaProtegida
{
    private static readonly HashSet<string> Prontas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cria a pasta (se faltar) e põe as permissões; uma vez por pasta enquanto o app está aberto.</summary>
    public static void Garantir(string pasta)
    {
        lock (Prontas)
        {
            if (Prontas.Contains(pasta) && Directory.Exists(pasta))
                return;
            var info = Directory.CreateDirectory(pasta);
            info.SetAccessControl(SoDoUsuario());
            Prontas.Add(pasta);
        }
    }

    /// <summary>As permissões da pasta: só o usuário atual, sem herança (as de dentro herdam estas).</summary>
    public static DirectorySecurity SoDoUsuario()
    {
        SecurityIdentifier usuario = WindowsIdentity.GetCurrent().User!;
        var seguranca = new DirectorySecurity();
        seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        seguranca.SetOwner(usuario);
        seguranca.AddAccessRule(new FileSystemAccessRule(usuario, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return seguranca;
    }

    /// <summary>Quem pode entrar na pasta (para os testes conferirem).</summary>
    public static IReadOnlyList<string> QuemEntra(string pasta)
    {
        var regras = new DirectoryInfo(pasta).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        return [.. regras.Cast<FileSystemAccessRule>().Select(r => r.IdentityReference.Value).Distinct()];
    }

    /// <summary>Se a pasta herda permissões da de cima (não deveria).</summary>
    public static bool Herda(string pasta) => !new DirectoryInfo(pasta).GetAccessControl().AreAccessRulesProtected;
}
