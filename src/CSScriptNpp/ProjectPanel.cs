﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CSScriptLibrary;
using System.Reflection;

namespace CSScriptNpp
{
    public partial class ProjectPanel : Form
    {
        static internal string currentScript;

        public ProjectPanel()
        {
            InitializeComponent();

            RefreshControls();
        }

        void runBtn_Click(object sender, EventArgs e)
        {
            Run();
        }

        void EditItem(string scriptFile)
        {
            Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_DOOPEN, 0, scriptFile);
            Win32.SendMessage(Npp.CurrentScintilla, SciMsg.SCI_GRABFOCUS, 0, 0);
        }

        void newBtn_Click(object sender, EventArgs e)
        {
            using (var input = new ScriptNameInput())
            {
                if (input.ShowDialog() != DialogResult.OK)
                    return;

                string scriptDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "C# Scripts");

                if (!Directory.Exists(scriptDir))
                    Directory.CreateDirectory(scriptDir);

                string scriptName = NormalizeScriptName(input.ScriptName ?? "New Script");

                int index = Directory.GetFiles(scriptDir, scriptName + "*.cs").Length;

                string newScript = Path.Combine(scriptDir, scriptName + ".cs");
                if (index != -1)
                {
                    int count = 0;
                    do
                    {
                        index++;
                        count++;
                        newScript = Path.Combine(scriptDir, string.Format("{0}{1}.cs", scriptName, index));
                        if (count > 10)
                        {
                            MessageBox.Show("Too many script files with the similar name already exists.\nPlease specify a different file name or clean up some existing scripts.", "CS-Script");
                        }
                    }
                    while (File.Exists(newScript));
                }

                string scriptCode = (Config.Instance.ClasslessScriptByDefault ? defaultClasslessScriptCode : defaultScriptCode);
                if (!File.Exists(newScript))
                {
                    File.WriteAllText(newScript, scriptCode);
                    Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_DOOPEN, 0, newScript);
                    Win32.SendMessage(Npp.CurrentScintilla, SciMsg.SCI_GRABFOCUS, 0, 0);

                    loadBtn.PerformClick();
                }
                else
                {
                    Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_MENUCOMMAND, 0, NppMenuCmd.IDM_FILE_NEW);
                    Win32.SendMessage(Npp.CurrentScintilla, SciMsg.SCI_GRABFOCUS, 0, 0);
                    Win32.SendMessage(Npp.CurrentScintilla, SciMsg.SCI_ADDTEXT, scriptCode.Length, scriptCode);

                    //for some reason setting the lexer does not work
                    int SCLEX_CPP = 3;
                    Win32.SendMessage(Npp.CurrentScintilla, SciMsg.SCI_SETLEXER, SCLEX_CPP, 0);
                    Win32.SendMessage(Npp.CurrentScintilla, SciMsg.SCI_SETLEXERLANGUAGE, 0, "cpp");
                }
            }
        }

        string NormalizeScriptName(string text)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) + "_";

            foreach (char c in invalid)
            {
                text = text.Replace(c.ToString(), "");
            }

            return text;
        }

        void SelectScript(string scriptFile)
        {
            if (treeView1.Nodes.Count > 0)
                foreach (TreeNode item in treeView1.Nodes[0].Nodes)
                    if (item.Tag is ProjectItem && string.Compare((item.Tag as ProjectItem).File, scriptFile, true) == 0)
                    {
                        treeView1.SelectedNode = item;
                        treeView1.Focus();
                        return;
                    }
        }

        const string defaultScriptCode =
@"using System;
using System.Diagnostics;
using System.Windows.Forms;

class Script
{
	[STAThread]
	static public void Main(string[] args)
	{
        Console.WriteLine(""Hello World!"");
	}
}";
        const string defaultClasslessScriptCode =
@"//css_args /ac
using System;

void main(string[] args)
{
    Console.WriteLine(""Hello World!"");
}";

        void synchBtn_Click(object sender, EventArgs e)
        {
            string path;
            Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_GETFULLCURRENTPATH, 0, out path);
            SelectScript(path);
        }

        void validateBtn_Click(object sender, EventArgs e)
        {
            Build();
        }

        void debugBtn_Click(object sender, EventArgs e)
        {
            Debug();
        }

        public void RunAsExternal()
        {
            Run(true);
        }

        public void Run(bool asExternal = false)
        {
            if (currentScript == null)
                loadBtn.PerformClick();

            if (currentScript == null)
            {
                MessageBox.Show("Please load some script file first.", "CS-Script");
            }
            else
            {
                try
                {
                    EditItem(currentScript);
                    Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_SAVECURRENTFILE, 0, 0);

                    if (asExternal)
                    {
                        try
                        {
                            CSSctiptHelper.ExecuteAsynch(currentScript);
                        }
                        catch (Exception e)
                        {
                            Plugin.ShowOutputPanel()
                                  .ShowBuildOutput()
                                  .WriteLine(e.Message)
                                  .SetCaretAtStart();
                        }
                    }
                    else
                    {
                        OutputPanel outputPanel = Plugin.ShowOutputPanel();

                        outputPanel.AttachDebuger();
                        outputPanel.ConsoleOutput.Clear();
                        outputPanel.BuildOutput.Clear();
                        outputPanel.DebugOutput.Clear();

                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                outputPanel.ShowDebugOutput();
                                if (Config.Instance.InterceptConsole)
                                {
                                    CSSctiptHelper.Execute(currentScript, OnRunStart, OnConsoleOut);
                                }
                                else
                                {
                                    CSSctiptHelper.Execute(currentScript, OnRunStart);
                                }
                            }
                            catch (Exception e)
                            {
                                outputPanel.ShowBuildOutput()
                                           .WriteLine(e.Message)
                                           .SetCaretAtStart();
                            }
                            finally
                            {
                                this.InUiThread(() =>
                                {
                                    Plugin.RunningScript = null;
                                    RefreshControls();
                                });
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Plugin.ShowOutputPanel()
                          .ShowBuildOutput()
                          .WriteLine(ex.Message)
                          .SetCaretAtStart();
                }
            }
        }

        public void Debug()
        {
            if (currentScript == null)
                loadBtn.PerformClick();

            if (currentScript == null)
            {
                MessageBox.Show("Please load some script file first.", "CS-Script");
            }
            else
            {
                Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_SAVECURRENTFILE, 0, 0);
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        CSSctiptHelper.ExecuteDebug(currentScript);
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(e.Message);
                    }
                });
            }
        }

        public void OpenInVS()
        {
            Cursor = Cursors.WaitCursor;
            if (currentScript == null)
            {
                MessageBox.Show("Please load some script file first.", "CS-Script");
            }
            else
            {
                Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_SAVECURRENTFILE, 0, 0);
                try
                {
                    CSSctiptHelper.OpenAsVSProjectFor(currentScript);
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message);
                }
            }
            Cursor = Cursors.Default;
        }

        void OnRunStart(Process proc)
        {
            Plugin.RunningScript = proc;
            this.Invoke((Action)RefreshControls);
        }

        void OnConsoleOut(string line)
        {
            if (Plugin.OutputPanel.ConsoleOutput.IsEmpty)
                Plugin.OutputPanel.ShowConsoleOutput();

            Plugin.OutputPanel.ConsoleOutput.WriteLine(line);
        }

        void Job()
        {
            try
            {
                CSScript.Compile(currentScript, null, false);
            }
            catch (Exception ex)
            {
                Environment.SetEnvironmentVariable("CSS_COMPILE_ERROR", ex.Message);
            }
        }

        public void Build()
        {
            lock (this)
            {
                if (currentScript == null)
                    loadBtn.PerformClick();

                if (currentScript == null)
                {
                    MessageBox.Show("Please load some script file first.", "CS-Script");
                }
                else
                {
                    OutputPanel outputPanel = Plugin.ShowOutputPanel();

                    outputPanel.ShowBuildOutput();
                    outputPanel.BuildOutput.Clear();
                    outputPanel.BuildOutput.WriteLine("------ Build started: Script: " + Path.GetFileNameWithoutExtension(currentScript) + " ------");

                    try
                    {
                        EditItem(currentScript);
                        Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_SAVECURRENTFILE, 0, 0);

                        CSSctiptHelper.Build(currentScript);

                        outputPanel.BuildOutput.WriteLine(null)
                                               .WriteLine("========== Build: succeeded ==========")
                                               .SetCaretAtStart();
                    }
                    catch (Exception ex)
                    {
                        outputPanel.ShowBuildOutput()
                                   .WriteLine(null)
                                   .WriteLine(ex.Message)
                                   .WriteLine("========== Build: Failed ==========")
                                   .SetCaretAtStart();
                    }
                }
            }
        }

        void reloadBtn_Click(object sender, EventArgs e)
        {
            LoadScript(currentScript);
        }

        void RefreshControls()
        {
            openInVsBtn.Visible = Utils.IsVS2010PlusAvailable;

            newBtn.Enabled = true;

            validateBtn.Enabled =
            reloadBtn.Enabled =
            debugBtn.Enabled =
            openInVsBtn.Enabled =
            synchBtn.Enabled =
            runBtn.Enabled = (treeView1.Nodes.Count > 0);

            bool running = (Plugin.RunningScript != null);
            runBtn.Visible = !running;
            stopBtn.Visible = running;

            if (running)
            {
                validateBtn.Enabled =
                debugBtn.Enabled =
                openInVsBtn.Enabled =
                loadBtn.Enabled =
                newBtn.Enabled =
                reloadBtn.Enabled = false;
            }
            else
                loadBtn.Enabled = true;
        }

        ProjectItem SelectedItem
        {
            get
            {
                if (treeView1.SelectedNode != null)
                    return treeView1.SelectedNode.Tag as ProjectItem;
                else
                    return null;
            }
        }

        void openInVsBtn_Click(object sender, EventArgs e)
        {
            OpenInVS();
        }

        void aboutBtn_Click(object sender, EventArgs e)
        {
            Plugin.ShowAbout();
        }

        void hlpBtn_Click(object sender, EventArgs e)
        {
            try
            {
                MessageBox.Show("Not Implemented Yet");
                //Process.Start("https://dl.dropboxusercontent.com/u/2192462/NPP/NppScriptsHelp.html");
            }
            catch { }
        }

        void loadBtn_Click(object sender, EventArgs e)
        {
            string path;
            Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_GETFULLCURRENTPATH, 0, out path);
            if (!File.Exists(path))
                Win32.SendMenuCmd(Npp.NppHandle, NppMenuCmd.IDM_FILE_SAVE, 0);

            Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_GETFULLCURRENTPATH, 0, out path);
            if (File.Exists(path))
                LoadScript(path);

            RefreshControls();

            Task.Factory.StartNew(CSSctiptHelper.ClearVSDir);
        }

        const int scriptImage = 1;
        const int folderImage = 0;
        const int assemblyImage = 2;
        const int includeImage = 3;

        void UnloadScript()
        {
            currentScript = null;
            treeView1.Nodes.Clear();
        }

        void LoadScript(string scriptFile)
        {
            if (!string.IsNullOrWhiteSpace(scriptFile) && File.Exists(scriptFile))
            {
                if (!scriptFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("The file type '" + Path.GetExtension(scriptFile) + "' is not supported.", "CS-Script");
                }
                else
                {
                    try
                    {
                        Project project = CSSctiptHelper.GenerateProjectFor(scriptFile);

                        treeView1.BeginUpdate();
                        treeView1.Nodes.Clear();

                        TreeNode root = treeView1.Nodes.Add("Script '" + Path.GetFileNameWithoutExtension(scriptFile) + "'");
                        TreeNode references = root.Nodes.Add("References");

                        root.SelectedImageIndex =
                        root.ImageIndex = scriptImage;
                        references.SelectedImageIndex =
                        references.ImageIndex = assemblyImage;
                        references.ContextMenuStrip = itemContextMenu;

                        root.ContextMenuStrip = solutionContextMenu;
                        root.ToolTipText = "Script: " + scriptFile;

                        Action<TreeNode, string[]> populateNode =
                            (node, files) =>
                            {
                                foreach (var file in files)
                                {
                                    int imageIndex = includeImage;
                                    var info = new ProjectItem(file) { IsPrimary = (file == project.PrimaryScript) };
                                    if (info.IsPrimary)
                                        imageIndex = scriptImage;
                                    if (info.IsAssembly)
                                        imageIndex = assemblyImage;
                                    node.Nodes.Add(new TreeNode(info.Name) { ImageIndex = imageIndex, SelectedImageIndex = imageIndex, Tag = info, ToolTipText = file, ContextMenuStrip = itemContextMenu });
                                };
                            };

                        populateNode(references, project.Assemblies);
                        populateNode(root, project.SourceFiles);
                        root.Expand();

                        treeView1.EndUpdate();

                        currentScript = scriptFile;
                        Intellisense.Refresh();
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                MessageBox.Show("Script '" + scriptFile + "' does not exist.", "CS-Script");
            }
        }

        void outputBtn_Click(object sender, EventArgs e)
        {
            Plugin.DoOutputPanel();
        }

        void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (SelectedItem != null && !SelectedItem.IsAssembly)
            {
                EditItem(SelectedItem.File);
                Win32.SendMessage(Npp.NppHandle, NppMsg.NPPM_SAVECURRENTFILE, 0, 0);
            }
        }

        void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            RefreshControls();
        }

        void stopBtn_Click(object sender, EventArgs e)
        {
            if (Plugin.RunningScript != null)
            {
                try
                {
                    Plugin.RunningScript.Kill();
                }
                catch (Exception ex)
                {
                    Plugin.OutputPanel.BuildOutput.WriteLine(null)
                                                  .WriteLine(ex.Message);
                }
            }
        }

        void ProjectPanel_KeyDown(object sender, KeyEventArgs e)
        {
        }

        void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
            if (e.Node == treeView1.Nodes[0].Nodes[0])
            {
                e.Node.ImageIndex = e.Node.SelectedImageIndex = folderImage;
            }
        }

        void treeView1_AfterCollapse(object sender, TreeViewEventArgs e)
        {
            if (e.Node == treeView1.Nodes[0].Nodes[0])
            {
                e.Node.ImageIndex = e.Node.SelectedImageIndex = assemblyImage;
            }
        }

        void unloadScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UnloadScript();
        }

        void openCommandPromptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WithSelectedNodeProjectItem(item =>
                {
                    string file;
                    if (treeView1.SelectedNode == treeView1.Nodes[0]) //root node
                        file = currentScript;
                    else if (item != null)
                        file = item.File;
                    else
                        return;

                    string path = Path.GetDirectoryName(file);

                    if (Directory.Exists(path))
                        Process.Start("cmd.exe", "/K \"cd " + path + "\"");
                    else
                        MessageBox.Show("Directory '" + path + "' does not exist.", "CS-Script");
                });
        }

        void openContainingFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WithSelectedNodeProjectItem(item =>
            {
                if (item != null)
                {
                    string path = item.File;
                    if (File.Exists(path))
                        Process.Start("explorer.exe", "/select," + path);
                    else
                        MessageBox.Show("File '" + path + "' does not exist.", "CS-Script");
                }
            });
        }

        void WithSelectedNodeProjectItem(Action<ProjectItem> action)
        {
            if (treeView1.SelectedNode != null)
            {
                try
                {
                    action(treeView1.SelectedNode.Tag as ProjectItem);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), "CS-Script");
                }
            }
        }

        void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = e.Node;
            }
        }
    }
}