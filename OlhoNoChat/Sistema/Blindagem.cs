using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OlhoNoChat.Sistema;

/// <summary>
/// Proteções do próprio processo, ligadas no começo do app (a sessão do TikTok fica na memória enquanto ele lê):
/// <list type="bullet">
/// <item>outros programas, mesmo do mesmo usuário, não conseguem ler nem escrever na memória do app, nem injetar código
/// nele (as permissões do processo só deixam ver se ele está aberto e fechá-lo);</item>
/// <item>DLLs só da pasta do app e do Windows (nunca da pasta atual nem do PATH, onde alguém poderia plantar uma);</item>
/// <item>sem DLLs de pastas de rede ou de baixa confiança, e sem os "ganchos" antigos do Windows que carregam DLLs de
/// outros programas em todo processo (AppInit_DLLs e afins).</item>
/// </list>
/// Cada uma que falhar fica no registro e o app segue (nenhuma é essencial para ler o chat).
/// </summary>
public static class Blindagem
{
    /// <summary>As proteções que ligaram (para a tela e o registro).</summary>
    public static IReadOnlyList<string> Ligadas => _ligadas;
    private static readonly List<string> _ligadas = [];

    public static void Ligar()
    {
        Tentar("DLLs só da pasta do app e do Windows", () =>
            Checar(SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)));

        Tentar("sem ganchos antigos do Windows", () =>
        {
            var p = new PROCESS_MITIGATION_FLAGS { Flags = 1 }; // DisableExtensionPoints
            Checar(SetProcessMitigationPolicy(ProcessExtensionPointDisablePolicy, ref p, Marshal.SizeOf<PROCESS_MITIGATION_FLAGS>()));
        });

        Tentar("sem DLLs de rede ou de baixa confiança", () =>
        {
            // NoRemoteImages | NoLowMandatoryLabelImages | PreferSystem32Images
            var p = new PROCESS_MITIGATION_FLAGS { Flags = 1 | 2 | 4 };
            Checar(SetProcessMitigationPolicy(ProcessImageLoadPolicy, ref p, Marshal.SizeOf<PROCESS_MITIGATION_FLAGS>()));
        });

        Tentar("memória fechada para outros programas", FecharOProcesso);

        System.Diagnostics.Debug.WriteLine("Proteções ligadas: " + string.Join("; ", _ligadas) + ".");
    }

    // As permissões do processo: o usuário só pode ver o que ele é, esperar ele fechar e fechá-lo; o SYSTEM, tudo.
    // Sem PROCESS_VM_READ/WRITE, CREATE_THREAD, DUP_HANDLE etc., outro programa sem privilégio de administrador não lê
    // a memória nem injeta código.
    private static void FecharOProcesso()
    {
        string usuario = WindowsIdentity.GetCurrent().User!.Value;
        // 0x1000 PROCESS_QUERY_LIMITED_INFORMATION | 0x400 PROCESS_QUERY_INFORMATION | 0x100000 SYNCHRONIZE |
        // 0x1 PROCESS_TERMINATE. As de consulta só dizem o que o processo é (caminho, memória usada), nunca o conteúdo
        // dela; sem a 0x400 o Gerenciador de Tarefas, o taskkill e o atualizador não conseguiam fechar o app
        string sddl = $"D:P(A;;0x101401;;;{usuario})(A;;0x1FFFFF;;;SY)";
        Checar(ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out IntPtr descritor, IntPtr.Zero));
        try
        {
            Checar(GetSecurityDescriptorDacl(descritor, out bool temDacl, out IntPtr dacl, out _));
            if (!temDacl)
                throw new Win32Exception("Sem DACL.");
            int erro = SetSecurityInfo(GetCurrentProcess(), SE_KERNEL_OBJECT, DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
            if (erro != 0)
                throw new Win32Exception(erro);
        }
        finally
        {
            LocalFree(descritor);
        }
    }

    private static void Tentar(string nome, Action acao)
    {
        try
        {
            acao();
            _ligadas.Add(nome);
        }
        catch (Exception e) when (e is Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"Proteção \"{nome}\" não ligou: {e.Message}");
        }
    }

    private static void Checar(bool ok)
    {
        if (!ok)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x1000;
    private const int ProcessExtensionPointDisablePolicy = 6;
    private const int ProcessImageLoadPolicy = 10;
    private const int SE_KERNEL_OBJECT = 6;
    private const uint DACL_SECURITY_INFORMATION = 0x4;
    private const uint PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MITIGATION_FLAGS
    {
        public uint Flags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessMitigationPolicy(int policy, ref PROCESS_MITIGATION_FLAGS buffer, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memoria);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revisao, out IntPtr descritor, IntPtr tamanho);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr descritor, out bool temDacl, out IntPtr dacl, out bool padrao);

    [DllImport("advapi32.dll")]
    private static extern int SetSecurityInfo(IntPtr objeto, int tipo, uint informacao, IntPtr dono, IntPtr grupo, IntPtr dacl, IntPtr sacl);
}
