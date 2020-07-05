﻿using ICSharpCode.TextEditor;
using LOU;
using Microsoft.Win32;
using SharpMonoInjector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace EasyLOU
{
    public partial class MainForm : Form
    {
        public static MainForm TheMainForm;

        public static int CurrentClientProcessId = -1;

        private static String ClientStatusMemoryMapMutexName;
        private static String ClientStatusMemoryMapName;
        private static Int32 ClientStatusMemoryMapSize;
        public static MemoryMap ClientStatusMemoryMap;

        private static String ClientCommandsMemoryMapMutexName;
        private static String ClientCommandsMemoryMapName;
        private static Int32 ClientCommandsMemoryMapSize;
        public static MemoryMap ClientCommandsMemoryMap;

        public static object ClientStatusLock = new object();
        public static ClientStatus ClientStatus;

        private static Dictionary<String, ScriptDebugger> ScriptDebuggers = new Dictionary<string, ScriptDebugger>();

        public MainForm()
        {
            MainForm.TheMainForm = this;
            InitializeComponent();
            this.Text = "EasyLOU - " + Assembly.GetExecutingAssembly().GetName().Version.ToString();
            ConnectFirstClientAfter(-1);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N))
            {
                DoNew();
                return true;
            }
            else if (keyData == (Keys.Control | Keys.O))
            {
                DoOpen();
                return true;
            }
            else if (keyData == (Keys.Control | Keys.S))
            {
                DoSave();
                return true;
            }
            else if (keyData == (Keys.Control | Keys.Q))
            {
                DoExit();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateVar(String Key, String Value)
        {
            if (!VarsTreeView.Nodes.ContainsKey(Key))
                VarsTreeView.Nodes.Add(Key, Key + ": " + Value);
            else
                VarsTreeView.Nodes[Key].Text = Key + ": " + Value;

        }
        private void UpdateAttribute(String ParentKey, String Key, String Value)
        {
            if (!StatusTreeView.Nodes.ContainsKey(ParentKey))
            {
                StatusTreeView.Nodes.Add(ParentKey, ParentKey);
                StatusTreeView.Nodes[ParentKey].Expand();
            }

            if (!StatusTreeView.Nodes[ParentKey].Nodes.ContainsKey(Key))
                StatusTreeView.Nodes[ParentKey].Nodes.Add(Key, Key + ": " + Value);
            else
                StatusTreeView.Nodes[ParentKey].Nodes[Key].Text = Key + ": " + Value;
        }

        private void UpdateAttributesGroup(Dictionary<String, String> Group, String GroupName)
        {
            foreach (KeyValuePair<String, String> Attribute in Group)
            {
                UpdateAttribute(GroupName, Attribute.Key, (Attribute.Value != null ? Attribute.Value.ToString() : ""));
            }
        }

        public delegate void UpdateStatusTreeViewDelegate();
        public void UpdateStatusTreeView()
        {
            lock (ClientStatusLock)
            {
                StatusTreeView.BeginUpdate();
                UpdateAttribute("Debug Info", "Timestamp", ClientStatus.TimeStamp.ToString());
                if (new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds() - ClientStatus.TimeStamp <= 5000)
                {
                    MainStatusLabel.Text = "Connected to " + MainForm.CurrentClientProcessId.ToString() + ".";
                    UpdateAttributesGroup(ClientStatus.CharacterInfo, "Character Info");
                    UpdateAttributesGroup(ClientStatus.StatusBar, "Status Bar");
                    UpdateAttributesGroup(ClientStatus.ContainerInfo, "Container Info");
                    UpdateAttributesGroup(ClientStatus.LastAction, "Last Action");
                    UpdateAttributesGroup(ClientStatus.Find, "Find");
                    UpdateAttributesGroup(ClientStatus.ShopInfo, "Shop Info");
                    UpdateAttributesGroup(ClientStatus.ExtendedInfo, "Extended Info");
                    UpdateAttributesGroup(ClientStatus.ClientInfo, "Client Info");
                    UpdateAttributesGroup(ClientStatus.CombatInfo, "Combat Info");
                    UpdateAttributesGroup(ClientStatus.TileInfo, "Tile Info");
                    UpdateAttributesGroup(ClientStatus.TimeInfo, "Time Info");
                    UpdateAttributesGroup(ClientStatus.Miscellaneous, "Miscellaneous");
                    UpdateAttributesGroup(ClientStatus.ResultVariables, "Result Variables");
                }
                else
                {
                    MainStatusLabel.Text = "Client " + MainForm.CurrentClientProcessId.ToString() + " not responding!";
                }
                StatusTreeView.EndUpdate();
            }
        }

        public static void RefreshClientStatus()
        {
            if (MainForm.CurrentClientProcessId == -1 || MainForm.ClientStatusMemoryMap == null)
                return;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            lock (MainForm.ClientStatusLock)
            {
                MainForm.ClientStatusMemoryMap.ReadMemoryMap<ClientStatus>(out MainForm.ClientStatus);
            }

            if (MainForm.TheMainForm != null && MainForm.ClientStatus != null)
            {
                MainForm.TheMainForm.Invoke(new MainForm.ResetTimerReadClientStatusDelegate(MainForm.TheMainForm.ResetTimerReadClientStatus));
                MainForm.TheMainForm.Invoke(new MainForm.UpdateStatusTreeViewDelegate(MainForm.TheMainForm.UpdateStatusTreeView));
            }

            stopwatch.Stop();
            //Console.WriteLine("RefreshClientStatus completed in {0} ms", stopwatch.ElapsedMilliseconds);
        }

        public delegate void ResetTimerReadClientStatusDelegate();
        public void ResetTimerReadClientStatus()
        {
            this.TimerReadClientStatus.Stop();
            this.TimerReadClientStatus.Start();
        }

        private void TimerReadClientStatus_Tick(object sender, EventArgs e)
        {
            MainForm.RefreshClientStatus();
        }

        private void TimerRefreshScriptVars_Tick(object sender, EventArgs e)
        {
            String guid = ScriptsTab.SelectedTab.Tag.ToString();
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptDebugger Debugger;
            if (ScriptDebuggers.TryGetValue(guid, out Debugger))
            {
                if (Debugger.vars != null && Debugger.vars.Count > 0)
                {
                    VarsTreeView.BeginUpdate();
                    lock (Debugger.varsLock)
                    {
                        foreach (var v in Debugger.vars)
                        {
                            UpdateVar(v.Key, v.Value);
                        }
                    }
                    VarsTreeView.EndUpdate();
                }
            }
            else
            {
                VarsTreeView.Nodes.Clear();
            }
        }
        #region Actions

        private void DoNew()
        {
            TabPage NewScriptTab = new TabPage();
            SplitContainer NewScriptSplit = new System.Windows.Forms.SplitContainer();
            TextEditorControlEx NewScriptTextArea = new TextEditorControlEx();
            TextBox NewScriptOutput = new System.Windows.Forms.TextBox();

            // 
            // NewScriptTab
            // 
            NewScriptTab.Controls.Add(NewScriptSplit);
            NewScriptTab.Location = new System.Drawing.Point(4, 29);
            NewScriptTab.Name = "NewScriptTab";
            NewScriptTab.Padding = new System.Windows.Forms.Padding(3);
            NewScriptTab.Size = new System.Drawing.Size(492, 300);
            NewScriptTab.TabIndex = 0;
            NewScriptTab.Text = "new";
            NewScriptTab.Tag = Guid.NewGuid().ToString();
            NewScriptTab.UseVisualStyleBackColor = true;
            // 
            // ScriptSplit
            // 
            NewScriptSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            NewScriptSplit.Location = new System.Drawing.Point(3, 3);
            NewScriptSplit.Name = "ScriptSplit";
            NewScriptSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // ScriptSplit.Panel1
            // 
            NewScriptSplit.Panel1.Controls.Add(NewScriptTextArea);
            // 
            // ScriptSplit.Panel2
            // 
            NewScriptSplit.Panel2.Controls.Add(NewScriptOutput);
            NewScriptSplit.Size = new System.Drawing.Size(486, 286);
            NewScriptSplit.SplitterDistance = 143;
            NewScriptSplit.TabIndex = 1;
            // 
            // NewScriptTextArea
            // 
            NewScriptTextArea.ContextMenuEnabled = true;
            NewScriptTextArea.ContextMenuShowDefaultIcons = true;
            NewScriptTextArea.ContextMenuShowShortCutKeys = true;
            NewScriptTextArea.Dock = System.Windows.Forms.DockStyle.Fill;
            NewScriptTextArea.FoldingStrategy = "Indent";
            NewScriptTextArea.Font = new System.Drawing.Font("Courier New", 10F);
            NewScriptTextArea.HideVScrollBarIfPossible = true;
            NewScriptTextArea.Location = new System.Drawing.Point(3, 3);
            NewScriptTextArea.Name = "ScriptTextArea";
            NewScriptTextArea.Size = new System.Drawing.Size(486, 294);
            NewScriptTextArea.SyntaxHighlighting = "Lua";
            NewScriptTextArea.TabIndex = 0;
            NewScriptTextArea.TextChanged += ScriptTextArea_TextChanged;
            NewScriptTextArea.Tag = "new";
            // 
            // ScriptOutput
            // 
            NewScriptOutput.Dock = System.Windows.Forms.DockStyle.Fill;
            NewScriptOutput.Location = new System.Drawing.Point(0, 0);
            NewScriptOutput.Multiline = true;
            NewScriptOutput.Name = "ScriptOutput";
            NewScriptOutput.Size = new System.Drawing.Size(486, 139);
            NewScriptOutput.TabIndex = 0;

            ScriptsTab.TabPages.Insert(ScriptsTab.TabPages.Count - 1, NewScriptTab);
            ScriptsTab.SelectedIndex = ScriptsTab.TabPages.Count - 2;
        }

        private void DoOpen()
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "LUA|*.lua|*.*|*.*";
                openFileDialog.Multiselect = false;
                if (openFileDialog.ShowDialog() == DialogResult.OK &&
                            openFileDialog.FileNames.Length > 0)
                {
                    String filePath = openFileDialog.FileName;
                    String fileName = Path.GetFileName(filePath);
                    DoNew();
                    ScriptsTab.SelectedTab.Text = fileName;
                    TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
                    ScriptTextArea.Tag = filePath;
                    ScriptTextArea.LoadFile(filePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

        }

        private void DoReopen()
        {
            MessageBox.Show("Not implemented!");
        }

        private void DoSave()
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            if ("new" == ScriptTextArea.Tag.ToString())
            {
                DoSaveAs();
            }
            else
            {
                try
                {
                    ScriptTextArea.SaveFile(ScriptTextArea.Tag.ToString());
                    ScriptTextArea.Document.UndoStack.ClearAll();
                    RefreshChangedStatus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
        }

        private void DoSaveAs()
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "LUA|*.lua|*.*|*.*";
                if (saveFileDialog.ShowDialog() == DialogResult.OK &&
                            saveFileDialog.FileNames.Length > 0)
                {
                    String filePath = saveFileDialog.FileName;
                    String fileName = Path.GetFileName(filePath);
                    TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
                    ScriptTextArea.Tag = filePath;
                    ScriptTextArea.SaveFile(filePath);
                    ScriptsTab.SelectedTab.Text = fileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void DoSaveAll()
        {
            int SelectedTabIndex = ScriptsTab.SelectedIndex;
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                ScriptsTab.SelectedIndex = TabIndex;
                DoSave();
            }
            ScriptsTab.SelectedIndex = SelectedTabIndex;
        }

        private void DoClose()
        {
            bool UnsavedChanges = false;

            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            if (ScriptTextArea.Document.UndoStack.UndoItemCount > 0)
            {
                UnsavedChanges = true;
            }

            if (UnsavedChanges)
            {
                if (MessageBox.Show("You have unsaved changes. Are you sure you want to close the current script?", "Close script with unsaved changes", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }
            }

            DoStop();

            ScriptsTab.SelectedTab.Controls.Clear();

            ScriptsTab.TabPages.Remove(ScriptsTab.SelectedTab);
        }

        private void DoCloseAll()
        {
            bool UnsavedChanges = false;
            int SelectedTabIndex = ScriptsTab.SelectedIndex;
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                TabPage ScriptPage = ScriptsTab.TabPages[TabIndex];
                TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptPage.Controls.Find("ScriptTextArea", true)[0]);
                if (ScriptTextArea.Document.UndoStack.UndoItemCount > 0)
                {
                    UnsavedChanges = true;
                    break;
                }
            }

            if (UnsavedChanges)
            {
                if (MessageBox.Show("You have unsaved changes. Are you sure you want to close all the open scripts?", "Close all script with unsaved changes", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }
            }

            DoStopAll();

            for (int TabIndex = ScriptsTab.TabCount - 2; TabIndex >= 0; TabIndex--)
            {
                ScriptsTab.TabPages[TabIndex].Controls.Clear();
                ScriptsTab.TabPages.RemoveAt(TabIndex);
            }
        }

        private void DoCloseAllButThis()
        {
            bool UnsavedChanges = false;
            int SelectedTabIndex = ScriptsTab.SelectedIndex;
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                if (TabIndex != ScriptsTab.SelectedIndex)
                {
                    TabPage ScriptPage = ScriptsTab.TabPages[TabIndex];
                    TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptPage.Controls.Find("ScriptTextArea", true)[0]);
                    if (ScriptTextArea.Document.UndoStack.UndoItemCount > 0)
                    {
                        UnsavedChanges = true;
                        break;
                    }
                }
            }

            if (UnsavedChanges)
            {
                if (MessageBox.Show("You have unsaved changes. Are you sure you want to close all the open scripts?", "Close all but this script with unsaved changes", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }
            }

            for (int TabIndex = ScriptsTab.TabCount - 2; TabIndex >= 0; TabIndex--)
            {
                if (TabIndex != ScriptsTab.SelectedIndex)
                {
                    ScriptDebugger Debugger;
                    if (ScriptDebuggers.TryGetValue(ScriptsTab.TabPages[TabIndex].Tag.ToString(), out Debugger))
                    {
                        Debugger.Stop();
                    }
                    RefreshToolStripStatus();
                    ScriptsTab.TabPages[TabIndex].Controls.Clear();
                    ScriptsTab.TabPages.RemoveAt(TabIndex);
                }
            }
        }

        private void DoExit()
        {
            bool UnsavedChanges = false;
            int SelectedTabIndex = ScriptsTab.SelectedIndex;
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                TabPage ScriptPage = ScriptsTab.TabPages[TabIndex];
                TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptPage.Controls.Find("ScriptTextArea", true)[0]);
                if (ScriptTextArea.Document.UndoStack.UndoItemCount > 0)
                {
                    UnsavedChanges = true;
                    break;
                }
            }

            if (UnsavedChanges)
            {
                if (MessageBox.Show("You have unsaved changes. Are you sure you want to exit?", "Exit with unsaved changes", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return;
                }
            }

            Application.Exit();
        }

        private void DoPlay()
        {
            String guid = ScriptsTab.SelectedTab.Tag.ToString();
            ScriptDebugger Debugger;
            if (ScriptDebuggers.ContainsKey(guid))
            {
                Debugger = ScriptDebuggers[guid];
                Debugger.Name = ScriptsTab.SelectedTab.Text;
            }
            else
            {
                Debugger = new ScriptDebugger(this, guid, ScriptsTab.SelectedTab.Text);
                ScriptDebuggers.Add(guid, Debugger);
            }
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            Debugger.Play(ScriptTextArea.Text, Path.GetDirectoryName(ScriptTextArea.Tag.ToString()));
            RefreshToolStripStatus();
        }
        private void DoPause()
        {
            ScriptDebugger Debugger;
            if (ScriptDebuggers.TryGetValue(ScriptsTab.SelectedTab.Tag.ToString(), out Debugger))
            {
                Debugger.Pause();
            }
            RefreshToolStripStatus();
        }

        private void DoStop()
        {
            ScriptDebugger Debugger;
            if (ScriptDebuggers.TryGetValue(ScriptsTab.SelectedTab.Tag.ToString(), out Debugger))
            {
                Debugger.Stop();
            }
            RefreshToolStripStatus();
        }

        private void DoStopAll()
        {
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                ScriptDebugger Debugger;
                if (ScriptDebuggers.TryGetValue(ScriptsTab.TabPages[TabIndex].Tag.ToString(), out Debugger))
                {
                    Debugger.Stop();
                }
            }
            RefreshToolStripStatus();
        }

        private void DoStopAllButThis()
        {
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                if (TabIndex != ScriptsTab.SelectedIndex)
                {
                    ScriptDebugger Debugger;
                    if (ScriptDebuggers.TryGetValue(ScriptsTab.TabPages[TabIndex].Tag.ToString(), out Debugger))
                    {
                        Debugger.Stop();
                    }
                }
            }
            RefreshToolStripStatus();
        }
        private void DoNewClient()
        {
            Object KeyValue = GetKey("ExePath");
            String ExePath = KeyValue != null ? KeyValue.ToString() : "";

            if (ExePath == "" || !File.Exists(ExePath))
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Application|*.exe";
                openFileDialog.Multiselect = false;
                if (openFileDialog.ShowDialog() == DialogResult.OK &&
                            openFileDialog.FileNames.Length > 0)
                {
                    ExePath = openFileDialog.FileName;
                    SetKey("ExePath", ExePath);

                }
                else
                {
                    return;
                }
            }

            bool started = false;

            try
            {
                var p = new Process();

                p.StartInfo.FileName = ExePath;
                p.StartInfo.WorkingDirectory = Path.GetDirectoryName(ExePath);

                MainStatusLabel.Text = ExePath + " starting...";

                started = p.Start();

                if (MessageBox.Show("Please press ok when the login screen is ready.", "Ready to inject", MessageBoxButtons.OKCancel) != DialogResult.OK)
                {
                    MainStatusLabel.Text = "Injection aborted.";
                    return;
                }

                System.Threading.Thread.Sleep(1000);

                var procId = p.Id;
                MainStatusLabel.Text = ExePath + " started, pid=" + procId.ToString();

                var monoModule = GetMonoModule(procId);

                Inject(procId, monoModule);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.ToString());
                SetKey("ExePath", "");
                started = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                SetKey("ExePath", "");
                started = false;
            }
        }

        private void DoSwapToNextClient()
        {
            ConnectFirstClientAfter(MainForm.CurrentClientProcessId);
        }

        private void DoVarDump()
        {

        }

        private void DoManageVarList()
        {
            MessageBox.Show("Not implemented!");
        }

        private void DoDontMoveCursor()
        {
            MessageBox.Show("Not implemented!");
        }

        private void DoHelp()
        {
            System.Diagnostics.Process.Start("https://lmgtfy.com/?q=easylou+help");
        }

        private void DoWebsite()
        {
            System.Diagnostics.Process.Start("https://lmgtfy.com/?q=easylou+website");
        }

        #endregion Actions

        #region Menus

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoNew();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoOpen();
        }

        private void reopenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoReopen();
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoSave();
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoSaveAs();
        }

        private void saveAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoSaveAll();
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoClose();
        }

        private void cloaseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoCloseAll();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoExit();
        }

        private void undoToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.Undo();
        }

        private void redoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.Redo();
        }

        private void cutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoCut();
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoCopy();
        }

        private void pateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoPaste();
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoDelete();
        }

        private void selectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoSelectAll();
        }

        private void findToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoFind();
        }

        private void replaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoReplace();
        }

        private void ScriptsTab_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ScriptsTab.SelectedIndex == ScriptsTab.TabPages.Count - 1)
            {
                DoNew();
                ScriptsTab.SelectedIndex = ScriptsTab.TabPages.Count - 2;
            }
            RefreshToolStripStatus();
            VarsTreeView.Nodes.Clear();
        }

        private void ScriptTextArea_TextChanged(object sender, EventArgs e)
        {
            RefreshChangedStatus();
            RefreshToolStripStatus();
        }

        private void helpToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            DoHelp();
        }

        private void websiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoWebsite();
        }

        private void newClientToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoNewClient();
        }

        private void swapToNextClientToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoSwapToNextClient();
        }

        private void varDumpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoVarDump();
        }

        private void manageVarListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DumpKeywords();
            DumpCommands();
            DoManageVarList();
        }

        private void dontMoveCursorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoDontMoveCursor();
        }

        private void RefreshChangedStatus()
        {
            int SelectedTabIndex = ScriptsTab.SelectedIndex;
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                TabPage ScriptPage = ScriptsTab.TabPages[TabIndex];
                TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptPage.Controls.Find("ScriptTextArea", true)[0]);
                if (ScriptTextArea.Document.UndoStack.UndoItemCount > 0)
                {
                    if (ScriptPage.Text.Substring(ScriptPage.Text.Length - 1, 1) != "*")
                    {
                        ScriptPage.Text += "*";
                    }
                }
                else
                {
                    if (ScriptPage.Text.Substring(ScriptPage.Text.Length - 1, 1) == "*")
                    {
                        ScriptPage.Text = ScriptPage.Text.Substring(0, ScriptPage.Text.Length - 1);
                    }
                }
            }
        }

        public delegate void RefreshToolStripStatusDelegate();
        public void RefreshToolStripStatus()
        {
            bool AtLeastOneRunning = false;
            String guid = ScriptsTab.SelectedTab.Tag.ToString();
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptDebugger Debugger;
            if (ScriptDebuggers.TryGetValue(guid, out Debugger))
            {
                switch (ScriptDebuggers[guid].Status)
                {
                    case ScriptDebugger.DebuggerStatus.Idle:
                    case ScriptDebugger.DebuggerStatus.Stopped:
                        {
                            if (ScriptTextArea.Text != "")
                                PlayToolStripButton.Enabled = true;
                            else
                                PlayToolStripButton.Enabled = false;
                            PauseToolStripButton.Enabled = false;
                            StopToolStripButton.Enabled = false;
                        }
                        break;

                    case ScriptDebugger.DebuggerStatus.Running:
                        {
                            AtLeastOneRunning = true;
                            PlayToolStripButton.Enabled = false;
                            PauseToolStripButton.Enabled = true;
                            StopToolStripButton.Enabled = true;
                        }
                        break;

                    case ScriptDebugger.DebuggerStatus.Paused:
                        {
                            if (ScriptTextArea.Text != "")
                                PlayToolStripButton.Enabled = true;
                            else
                                PlayToolStripButton.Enabled = false;
                            PauseToolStripButton.Enabled = false;
                            StopToolStripButton.Enabled = true;
                        }
                        break;
                }
            }
            else
            {
                if (ScriptTextArea.Text != "")
                    PlayToolStripButton.Enabled = true;
                else
                    PlayToolStripButton.Enabled = false;
                PauseToolStripButton.Enabled = false;
                StopToolStripButton.Enabled = false;
            }
            if (!AtLeastOneRunning)
            {
                for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
                {
                    TabPage ScriptPage = ScriptsTab.TabPages[TabIndex];
                    guid = ScriptPage.Tag.ToString();
                    if (ScriptDebuggers.TryGetValue(guid, out Debugger))
                    {
                        if (Debugger.Status == ScriptDebugger.DebuggerStatus.Running)
                            AtLeastOneRunning = true;
                    }
                }
            }
            if (AtLeastOneRunning)
            {
                StopAllToolStripButton.Enabled = true;
            }
            else
            {
                StopAllToolStripButton.Enabled = false;
            }
        }

        #endregion Menus

        #region PrimaryToolbar

        private void NewToolStripButton_Click(object sender, EventArgs e)
        {
            DoNew();
        }

        private void OpenToolStripButton_Click(object sender, EventArgs e)
        {
            DoOpen();
        }

        private void ReopenToolStripButton_Click(object sender, EventArgs e)
        {
            DoReopen();
        }

        private void SaveToolStripButton_Click(object sender, EventArgs e)
        {
            DoSave();
        }

        private void CloseToolStripButton_Click(object sender, EventArgs e)
        {
            DoClose();
        }

        private void CutToolStripButton_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoCut();
        }

        private void CopyToolStripButton_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoCopy();
        }

        private void PasteToolStripButton_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoPaste();
        }

        private void FindToolStripButton_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoFind();
        }

        private void ReplaceToolStripButton_Click(object sender, EventArgs e)
        {
            TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
            ScriptTextArea.DoReplace();

        }

        #endregion PrimaryToolbar

        #region SecondaryToolbar

        private void PlayToolStripButton_Click(object sender, EventArgs e)
        {
            DoPlay();
            RefreshToolStripStatus();
        }

        private void SwapCliToolStripButton_Click(object sender, EventArgs e)
        {
            DoSwapToNextClient();
        }

        private void NewCliToolStripButton_Click(object sender, EventArgs e)
        {
            DoNewClient();
        }

        private void HelpToolStripButton_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://lmgtfy.com/?q=easylou+help");
        }

        private void HomeToolStripButton_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://lmgtfy.com/?q=easylou+website");
        }

        #endregion SecondaryToolbar

        #region Injection

        private IntPtr GetMonoModule(int ProcessId)
        {
            MainStatusLabel.Text = "Getting Mono Module...";

            IntPtr MonoModule = new IntPtr();
            string Name;

            foreach (Process p in Process.GetProcesses())
            {
                if (p.Id != ProcessId)
                    continue;

                const ProcessAccessRights flags = ProcessAccessRights.PROCESS_QUERY_INFORMATION | ProcessAccessRights.PROCESS_VM_READ;
                IntPtr handle;

                if ((handle = Native.OpenProcess(flags, false, p.Id)) != IntPtr.Zero)
                {
                    if (ProcessUtils.GetMonoModule(handle, out IntPtr mono))
                    {
                        MonoModule = mono;
                        Name = p.ProcessName;
                    }

                    Native.CloseHandle(handle);
                }
            }

            MainStatusLabel.Text = "Process refreshed";

            return MonoModule;
        }

        private void Inject(int ProcessId, IntPtr MonoModule)
        {
            String AssemblyPath = "LOU.dll";

            IntPtr handle = Native.OpenProcess(ProcessAccessRights.PROCESS_ALL_ACCESS, false, ProcessId);

            if (handle == IntPtr.Zero)
            {
                MainStatusLabel.Text = "Failed to open process";
                return;
            }

            byte[] file;

            try
            {
                file = File.ReadAllBytes(AssemblyPath);
            }
            catch (IOException)
            {
                MainStatusLabel.Text = "Failed to read the file " + AssemblyPath;
                return;
            }

            MainStatusLabel.Text = "Injecting " + Path.GetFileName(AssemblyPath);

            using (Injector injector = new Injector(handle, MonoModule))
            {
                try
                {
                    IntPtr asm = injector.Inject(file, "LOU", "Loader", "Load");
                    MainStatusLabel.Text = "Injection on " + ProcessId.ToString() + " successful";
                    ConnectToClient(ProcessId);
                }
                catch (InjectorException ie)
                {
                    MainStatusLabel.Text = "Injection on " + ProcessId.ToString() + " failed: " + ie.Message;
                }
                catch (Exception e)
                {
                    MainStatusLabel.Text = "Injection failed (unknown error): " + e.Message;
                }
            }
        }

        private Object GetKey(String KeyName)
        {
            RegistryKey SoftwareKey = Registry.CurrentUser.OpenSubKey("Software", true);

            RegistryKey key = SoftwareKey.OpenSubKey("EasyLOU", true);

            if (key == null)
            {
                SoftwareKey.CreateSubKey("EasyLOU");
                key = SoftwareKey.OpenSubKey("EasyLOU", true);
            }

            return key.GetValue("ExePath");
        }

        private void SetKey(String KeyName, Object KeyValue)
        {
            RegistryKey SoftwareKey = Registry.CurrentUser.OpenSubKey("Software", true);

            RegistryKey key = SoftwareKey.OpenSubKey("EasyLOU", true);

            if (key == null)
            {
                SoftwareKey.CreateSubKey("EasyLOU");
                key = SoftwareKey.OpenSubKey("EasyLOU", true);
            }

            key.SetValue(KeyName, KeyValue);
        }

        #endregion Injection

        #region Multiple Clients
        private void ConnectFirstClientAfter(int CurrentProcessId)
        {
            List<int> pids = new List<int>();

            foreach (Process p in Process.GetProcesses())
            {
                if (p.ProcessName.Contains("Legends of Aria"))
                {
                    pids.Add(p.Id);
                }
            }

            pids.Sort();

            if (pids.Count > 0)
            {
                int next = (pids.IndexOf(CurrentProcessId) + 1) % pids.Count;
                ConnectToClient(pids.ElementAt(next));
            }
        }

        private void ConnectToClient(int ProcessId)
        {
            MainStatusLabel.Text += " Connecting to " + ProcessId.ToString() + "...";
            MainForm.CurrentClientProcessId = ProcessId;

            MainForm.ClientStatusMemoryMapMutexName = "ELOU_CS_MX_" + ProcessId.ToString();
            MainForm.ClientStatusMemoryMapName = "ELOU_CS_" + ProcessId.ToString();
            MainForm.ClientStatusMemoryMapSize = 1024 * 1024 * 10;
            MainForm.ClientStatusMemoryMap = new MemoryMap(MainForm.ClientStatusMemoryMapName, MainForm.ClientStatusMemoryMapSize, MainForm.ClientStatusMemoryMapMutexName);

            MainForm.ClientCommandsMemoryMapMutexName = "ELOU_CC_MX_" + ProcessId.ToString();
            MainForm.ClientCommandsMemoryMapName = "ELOU_CC_" + ProcessId.ToString();
            MainForm.ClientCommandsMemoryMapSize = 1024 * 1024;
            MainForm.ClientCommandsMemoryMap = new MemoryMap(MainForm.ClientCommandsMemoryMapName, MainForm.ClientCommandsMemoryMapSize, MainForm.ClientCommandsMemoryMapMutexName);
        }
        #endregion

        private void PauseToolStripButton_Click(object sender, EventArgs e)
        {
            DoPause();
        }

        private void StopToolStripButton_Click(object sender, EventArgs e)
        {
            DoStop();
        }


        private void StopAllToolStripButton_Click(object sender, EventArgs e)
        {
            DoStopAll();
        }

        private int CurrentSpin = 0;
        private void TimerSpinners_Tick(object sender, EventArgs e)
        {
            Char[] Spinners = "◴◷◶◵".ToCharArray();
            int SelectedTabIndex = ScriptsTab.SelectedIndex;
            for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; TabIndex++)
            {
                TabPage ScriptPage = ScriptsTab.TabPages[TabIndex];
                String guid = ScriptPage.Tag.ToString();
                ScriptDebugger Debugger;
                if (ScriptDebuggers.TryGetValue(guid, out Debugger))
                {
                    switch (Debugger.Status)
                    {
                        case ScriptDebugger.DebuggerStatus.Idle:
                        case ScriptDebugger.DebuggerStatus.Stopped:
                            {
                                if (Spinners.Contains(ScriptPage.Text[0]))
                                {
                                    ScriptPage.Text = ScriptPage.Text.Remove(0, 1);
                                }
                            }
                            break;
                        case ScriptDebugger.DebuggerStatus.Running:
                            {
                                if (Spinners.Contains(ScriptPage.Text[0]))
                                {
                                    ScriptPage.Text = ScriptPage.Text.Remove(0, 1);
                                }
                                ScriptPage.Text = Spinners[CurrentSpin] + ScriptPage.Text;
                            }
                            break;
                    }
                }
            }
            CurrentSpin = (CurrentSpin + 1) % 4;
        }

        private void StatusTreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (e.Node.Text.Contains(":"))
                {
                    StatusTreeView.SelectedNode = e.Node;
                    StatusTreeViewContextMenu.Show(Cursor.Position);
                }
            }
        }
        private void StatusTreeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.Node.Text.Contains(":"))
                {
                    int Separator = e.Node.Text.IndexOf(':');
                    String Value = e.Node.Text.Substring(Separator + 2, e.Node.Text.Length - 2 - Separator);
                    TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
                    ScriptTextArea.ActiveTextAreaControl.TextArea.InsertString(Value);
                }
            }
        }

        private void copyNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (StatusTreeView.SelectedNode != null && StatusTreeView.SelectedNode.Text.Contains(":"))
            {
                int Separator = StatusTreeView.SelectedNode.Text.IndexOf(':');
                string Value = StatusTreeView.SelectedNode.Text.Substring(0, Separator);
                Clipboard.SetText(Value);
            }
        }

        private void copyValueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (StatusTreeView.SelectedNode != null && StatusTreeView.SelectedNode.Text.Contains(":"))
            {
                int Separator = StatusTreeView.SelectedNode.Text.IndexOf(':');
                string Value = StatusTreeView.SelectedNode.Text.Substring(Separator + 2, StatusTreeView.SelectedNode.Text.Length - 2 - Separator);
                Clipboard.SetText(Value);
            }
        }

        private void closeThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoClose();
        }

        private void closeAllScriptsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoCloseAll();
        }

        private void closeAllScriptsButThisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DoCloseAllButThis();
        }

        private void ScriptsTab_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                for (int TabIndex = 0; TabIndex < ScriptsTab.TabCount - 1; ++TabIndex)
                {
                    if (ScriptsTab.GetTabRect(TabIndex).Contains(e.Location))
                    {
                        ScriptsTab.SelectedIndex = TabIndex;
                        ScriptsTabContextMenu.Show(Cursor.Position);
                    }
                }
            }
        }

        private void DumpKeywords()
        {
            // <Key word = "FINDITEMIDS" />
            string prefix = " <Key word = \"";
            string postfix = "\" />";
            Console.WriteLine("RefreshKeywords()");
            foreach (String s in (MainForm.ClientStatus.CharacterInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.CharacterInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.StatusBar.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.ContainerInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.LastAction.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.Find.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.ShopInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.ExtendedInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.ClientInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.CombatInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.TileInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.TimeInfo.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }
            foreach (String s in (MainForm.ClientStatus.Miscellaneous.Keys))
            {
                Console.WriteLine(prefix + s + postfix);
            }

            foreach (String s in (MainForm.ClientStatus.CharacterInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.CharacterInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.StatusBar.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.ContainerInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.LastAction.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.Find.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.ShopInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.ExtendedInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.ClientInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.CombatInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.TileInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.TimeInfo.Keys))
            {
                Console.WriteLine(s + ":");
            }
            foreach (String s in (MainForm.ClientStatus.Miscellaneous.Keys))
            {
                Console.WriteLine(s + ":");
            }
        }

        static void DumpCommands()
        {
            // <Key word = "FINDITEMIDS" />
            string prefix = " <Key word = \"";
            string postfix = "\" />";
            Console.WriteLine("RefreshKeywords()");

            string s = string.Join(",", Enum.GetNames(typeof(LOU.CommandType)));

            foreach (String c in Enum.GetNames(typeof(LOU.CommandType)))
            {
                Debug.Print("- [" + c + "](#" + c + ")");
            }

            foreach (String c in Enum.GetNames(typeof(LOU.CommandType)))
            {
                Debug.Print("## " + c);
                Debug.Print("Description");
            }

            foreach (String c in Enum.GetNames(typeof(LOU.CommandType)))
            {
                Debug.Print(prefix + c + postfix);
            }
        }

        public delegate void PrintOutputDelegate(String guid, String s);
        public void PrintOutput(String guid, String s)
        {
            foreach (TabPage page in ScriptsTab.TabPages)
            {
                if (page.Tag.ToString() == guid)
                {
                    Control[] ScriptOutput = page.Controls.Find("ScriptOutput", true);
                    if (ScriptOutput != null)
                    {
                        TextBox ScriptOutputTextBox = (TextBox)ScriptOutput[0];
                        if (ScriptOutputTextBox.Text.Length > 1024 * 1024 * 100)
                        {
                            ScriptOutputTextBox.Text = ScriptOutputTextBox.Text.Remove(0, 1024 * 1024 * 1);
                        }
                        ((TextBox)ScriptOutput[0]).AppendText(s + Environment.NewLine);
                    }
                    return;
                }
            }
        }

        private void VarsTreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (e.Node.Text.Contains(":"))
                {
                    VarsTreeView.SelectedNode = e.Node;
                    VarsTreeViewContextMenu.Show(Cursor.Position);
                }
            }
        }
        private void VarsTreeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.Node.Text.Contains(":"))
                {
                    int Separator = e.Node.Text.IndexOf(':');
                    String Value = e.Node.Text.Substring(Separator + 2, e.Node.Text.Length - 2 - Separator);
                    TextEditorControlEx ScriptTextArea = ((TextEditorControlEx)ScriptsTab.SelectedTab.Controls.Find("ScriptTextArea", true)[0]);
                    ScriptTextArea.ActiveTextAreaControl.TextArea.InsertString(Value);
                }
            }
        }

        private void toolStripMenuItem4_Click(object sender, EventArgs e)
        {
            if (VarsTreeView.SelectedNode != null && VarsTreeView.SelectedNode.Text.Contains(":"))
            {
                int Separator = VarsTreeView.SelectedNode.Text.IndexOf(':');
                string Value = VarsTreeView.SelectedNode.Text.Substring(0, Separator);
                Clipboard.SetText(Value);
            }
        }

        private void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            if (VarsTreeView.SelectedNode != null && VarsTreeView.SelectedNode.Text.Contains(":"))
            {
                int Separator = VarsTreeView.SelectedNode.Text.IndexOf(':');
                string Value = VarsTreeView.SelectedNode.Text.Substring(Separator + 2, VarsTreeView.SelectedNode.Text.Length - 2 - Separator);
                Clipboard.SetText(Value);
            }
        }
    }
}
