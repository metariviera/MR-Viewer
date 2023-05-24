/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class MapConsole : UserControl
    {
        RadegastInstance instance;
        GridClient client => instance.Client;
        bool Active => client.Network.Connected;
        WebBrowser map;
        MapControl mmap;
        Regex slscheme = new Regex("^secondlife://(.+)/([0-9]+)/([0-9]+)");
        bool InTeleport = false;
        bool mapCreated = false;
        Dictionary<string, ulong> regionHandles = new Dictionary<string, ulong>();

        public MapConsole(RadegastInstance inst)
        {
            InitializeComponent();
            Disposed += frmMap_Disposed;

            instance = inst;
            instance.ClientChanged += instance_ClientChanged;

            Visible = false;
            VisibleChanged += MapConsole_VisibleChanged;

            // Register callbacks
            RegisterClientEvents(client);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void RegisterClientEvents(GridClient client)
        {
            client.Grid.GridRegion += Grid_GridRegion;
            client.Self.TeleportProgress += Self_TeleportProgress;
            client.Network.SimChanged += Network_SimChanged;
            client.Friends.FriendFoundReply += Friends_FriendFoundReply;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Grid.GridRegion -= Grid_GridRegion;
            client.Self.TeleportProgress -= Self_TeleportProgress;
            client.Network.SimChanged -= Network_SimChanged;
            client.Friends.FriendFoundReply -= Friends_FriendFoundReply;
        }

        void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(client);
        }

        void createMap()
        {
            if (map == null)
            {
                mmap = new MapControl(instance);
                mmap.MapTargetChanged += (sender, e) =>
                {
                    txtRegion.Text = e.Region.Name;
                    nudX.Value = e.LocalX;
                    nudY.Value = e.LocalY;
                    lblStatus.Text = "Ready for " + e.Region.Name;
                };

                mmap.ZoomChanged += mmap_ZoomChaged;

                //if (instance.Netcom.Grid.ID == "agni")
                //{
                //    mmap.UseExternalTiles = true;
                //}
                mmap.Dock = DockStyle.Fill;
                pnlMap.Controls.Add(mmap);
                mmap_ZoomChaged(null, null);
                zoomTracker.Visible = true;
            }

        }

        void mmap_ZoomChaged(object sender, EventArgs e)
        {
            int newval = (int)(100f * (mmap.Zoom - mmap.MinZoom) / (mmap.MaxZoom - mmap.MinZoom));
            if (newval >= zoomTracker.Minimum && newval <= zoomTracker.Maximum)
                zoomTracker.Value = newval;
        }

        void map_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            map.DocumentCompleted -= map_DocumentCompleted;
            map.AllowWebBrowserDrop = false;
            map.WebBrowserShortcutsEnabled = false;
            map.ScriptErrorsSuppressed = true;
            map.ObjectForScripting = this;
            map.AllowNavigation = false;

            if (instance.MonoRuntime)
            {
                map.Navigating += map_Navigating;
            }

            ThreadPool.QueueUserWorkItem(sync =>
                {
                    Thread.Sleep(1000);
                    if (InvokeRequired && (!instance.MonoRuntime || IsHandleCreated))
                        BeginInvoke(new MethodInvoker(() =>
                            {
                                if (savedRegion != null)
                                {
                                    gotoRegion(savedRegion, savedX, savedY);
                                }
                                else if (Active)
                                {
                                    gotoRegion(client.Network.CurrentSim.Name, 128, 128);
                                }
                            }
                    ));
                }
            );
        }

        void frmMap_Disposed(object sender, EventArgs e)
        {
            // Unregister callbacks
            UnregisterClientEvents(client);
            instance.ClientChanged -= instance_ClientChanged;

            if (map != null)
            {
                if (instance.MonoRuntime)
                {
                    map.Navigating -= map_Navigating;
                }
                else
                {
                    map.Dispose();
                }
                map = null;
            }

            if (mmap != null)
            {
                mmap.Dispose();
                mmap = null;
            }
        }

        #region PublicMethods
        public void GoHome()
        {
            btnGoHome_Click(this, EventArgs.Empty);
        }

        public void DoNavigate(string region, string x, string y)
        {
            DisplayLocation(region, int.Parse(x), int.Parse(y), 0);
        }

        public void DisplayLocation(string region, int x, int y, int z)
        {
            txtRegion.Text = region;
            
            try
            {
                nudX.Value = x;
                nudY.Value = y;
                nudZ.Value = z;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed setting map position controls: ", Helpers.LogLevel.Warning, instance.Client, ex);
            }

            gotoRegion(txtRegion.Text, x, y);
            btnTeleport.Enabled = true;
            btnTeleport.Focus();
            lblStatus.Text = "Ready for " + region;
        }

        public void SetStatus(string msg)
        {
            lblStatus.Text = msg;
            btnTeleport.Enabled = false;
        }

        public void CenterOnGlobalPos(float gx, float gy, float z)
        {
            txtRegion.Text = string.Empty;

            nudX.Value = (int)gx % 256;
            nudY.Value = (int)gy % 256;
            nudZ.Value = (int)z;

            uint rx = (uint)(gx / 256);
            uint ry = (uint)(gy / 256);

            ulong hndle = Utils.UIntsToLong(rx * 256, ry * 256);
            targetRegionHandle = hndle;

            foreach (KeyValuePair<string, ulong> kvp in regionHandles)
            {
                if (kvp.Value == hndle)
                {
                    txtRegion.Text = kvp.Key;
                    btnTeleport.Enabled = true;
                }
            }
            mmap.CenterMap(rx, ry, (uint)gx % 256, (uint)gy % 256, true);
        }

        #endregion

        #region NetworkEvents

        void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Network_SimChanged(sender, e)));
                return;
            }

            gotoRegion(client.Network.CurrentSim.Name, (int)client.Self.SimPosition.X, (int)client.Self.SimPosition.Y);
            lblStatus.Text = $"Now in {client.Network.CurrentSim.Name}";
        }

        int lastTick = 0;

        void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Self_TeleportProgress(sender, e)));
                return;
            }

            switch (e.Status)
            {
                case TeleportStatus.Progress:
                    lblStatus.Text = "Progress: " + e.Message;
                    InTeleport = true;
                    break;

                case TeleportStatus.Start:
                    lblStatus.Text = "Teleporting to " + txtRegion.Text;
                    InTeleport = true;
                    break;

                case TeleportStatus.Cancelled:
                case TeleportStatus.Failed:
                    InTeleport = false;
                    lblStatus.Text = "Failed: " + e.Message;
                    if (Environment.TickCount - lastTick > 500)
                        instance.TabConsole.DisplayNotificationInChat("Teleport failed");
                    lastTick = Environment.TickCount;
                    break;

                case TeleportStatus.Finished:
                    lblStatus.Text = "Teleport complete";
                    if (Environment.TickCount - lastTick > 500)
                        instance.TabConsole.DisplayNotificationInChat("Teleport complete");
                    lastTick = Environment.TickCount;
                    InTeleport = false;
                    Network_SimChanged(null, null);
                    mmap?.ClearTarget();
                    break;

                default:
                    InTeleport = false;
                    break;
            }

            if (!InTeleport)
            {
                prgTeleport.Style = ProgressBarStyle.Blocks;
            }
        }

        void Grid_GridRegion(object sender, GridRegionEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Grid_GridRegion(sender, e)));
                return;
            }

            if (e.Region.RegionHandle == targetRegionHandle)
            {
                txtRegion.Text = e.Region.Name;
                btnTeleport.Enabled = true;
                targetRegionHandle = 0;
            }

            if (!string.IsNullOrEmpty(txtRegion.Text)
                && e.Region.Name.ToLower().Contains(txtRegion.Text.ToLower())
                && !lstRegions.Items.ContainsKey(e.Region.Name))
            {
                ListViewItem item = new ListViewItem(e.Region.Name) {Tag = e.Region, Name = e.Region.Name};
                lstRegions.Items.Add(item);
            }

            regionHandles[e.Region.Name] = Utils.UIntsToLong((uint)e.Region.X, (uint)e.Region.Y);

        }
        #endregion NetworkEvents

        void map_Navigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            e.Cancel = true;
            Regex r = new Regex(@"^(http://slurl.com/secondlife/|secondlife://)([^/]+)/(\d+)/(\d+)(/(\d+))?");
            Match m = r.Match(e.Url.ToString());

            if (m.Groups.Count > 3)
            {
                txtRegion.Text = m.Groups[2].Value;
                try
                {
                    nudX.Value = int.Parse(m.Groups[3].Value);
                    nudY.Value = int.Parse(m.Groups[4].Value);
                    nudZ.Value = 0;
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed setting map position controls: ", Helpers.LogLevel.Warning, instance.Client, ex);
                }

                if (m.Groups.Count > 5 && m.Groups[6].Value != String.Empty)
                {
                    nudZ.Value = int.Parse(m.Groups[6].Value);
                }
                BeginInvoke(new MethodInvoker(DoTeleport));
            }
        }

        void DoSearch()
        {
            if (!Active || txtRegion.Text.Length < 2) return;
            lstRegions.Clear();
            client.Grid.RequestMapRegion(txtRegion.Text, GridLayerType.Objects);

        }

        public void DoTeleport()
        {
            if (!Active) return;

            if (instance.MonoRuntime)
            {
                map?.Navigate(Path.GetDirectoryName(Application.ExecutablePath) + @"/worldmap.html");
            }

            lblStatus.Text = "Teleporting to " + txtRegion.Text;
            prgTeleport.Style = ProgressBarStyle.Marquee;

            ThreadPool.QueueUserWorkItem(state =>
                {
                    if (!client.Self.Teleport(txtRegion.Text, new Vector3((int)nudX.Value, (int)nudY.Value, (int)nudZ.Value)))
                    {
                        Self_TeleportProgress(this, new TeleportEventArgs(string.Empty, TeleportStatus.Failed, TeleportFlags.Default));
                    }
                    InTeleport = false;
                }
            );
        }

        #region JavascriptHooks
        string savedRegion = null;
        int savedX, savedY;

        void gotoRegion(string regionName, int simX, int simY)
        {
            savedRegion = regionName;
            savedX = simX;
            savedY = simY;

            if (!Visible) return;

            if (mmap != null)
            {
                if (!regionHandles.ContainsKey(regionName))
                {
                    ThreadPool.QueueUserWorkItem(sync =>
                        {
                            ManualResetEvent done = new ManualResetEvent(false);
                            EventHandler<GridRegionEventArgs> handler = (sender, e) =>
                                {
                                    regionHandles[e.Region.Name] = Utils.UIntsToLong((uint)e.Region.X, (uint)e.Region.Y);
                                    if (e.Region.Name == regionName)
                                        done.Set();
                                };
                            client.Grid.GridRegion += handler;
                            client.Grid.RequestMapRegion(regionName, GridLayerType.Objects);
                            if (done.WaitOne(30 * 1000, false))
                            {
                                if (!instance.MonoRuntime || IsHandleCreated)
                                    BeginInvoke(new MethodInvoker(() => gotoRegion(regionName, simX, simY)));
                            }
                            client.Grid.GridRegion -= handler;
                        }
                    );
                    return;
                }
                mmap.CenterMap(regionHandles[regionName], (uint)simX, (uint)simY, true);
                return;
            }

            if (map == null || map.Document == null) return;

            if (instance.MonoRuntime)
            {
                map.Document.InvokeScript(string.Format("gReg = \"{0}\"; gSimX = {1}; gSimY = {2}; monosucks", regionName, simX, simY));
            }
            else
            {
                map.Document.InvokeScript("gotoRegion", new object[] { regionName, simX, simY });
            }
        }

        #endregion

        #region GUIEvents

        private void txtRegion_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                txtRegion.Text = txtRegion.Text.Trim();
                e.SuppressKeyPress = true;
                DoSearch();
            }
        }

        private void lstRegions_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstRegions.SelectedItems.Count != 1)
            {
                btnTeleport.Enabled = false;
                return;
            }
            btnTeleport.Enabled = true;
            txtRegion.Text = lstRegions.SelectedItems[0].Text;
            lblStatus.Text = "Ready for " + txtRegion.Text;
            nudX.Value = 128;
            nudY.Value = 128;
            gotoRegion(txtRegion.Text, (int)nudX.Value, (int)nudY.Value);
        }

        private void lstRegions_Enter(object sender, EventArgs e)
        {
            lstRegions_SelectedIndexChanged(sender, e);
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Hide();
        }

        private void btnTeleport_Click(object sender, EventArgs e)
        {
            DoTeleport();
        }

        private void btnGoHome_Click(object sender, EventArgs e)
        {
            if (!Active) return;
            InTeleport = true;

            prgTeleport.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = "Teleporting home...";

            client.Self.RequestTeleport(UUID.Zero);
        }

        private void btnMyPos_Click(object sender, EventArgs e)
        {
            gotoRegion(client.Network.CurrentSim.Name, (int)client.Self.SimPosition.X, (int)client.Self.SimPosition.Y);
        }

        private void btnDestination_Click(object sender, EventArgs e)
        {
            if (txtRegion.Text != string.Empty)
            {
                gotoRegion(txtRegion.Text, (int)nudX.Value, (int)nudY.Value);
            }
        }

        private void MapConsole_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                ddOnlineFriends.Items.Clear();
                ddOnlineFriends.Items.Add("Online Friends");
                ddOnlineFriends.SelectedIndex = 0;

                var friends = client.Friends.FriendList.FindAll(f => f.CanSeeThemOnMap && f.IsOnline);
                if (friends != null)
                {
                    foreach (var f in friends)
                    {
                        if (f.Name != null)
                        {
                            ddOnlineFriends.Items.Add(f.Name);
                        }
                    }
                }
            }

            if (!mapCreated && Visible)
            {
                createMap();
                mapCreated = true;
            }
            else if (Visible && instance.MonoRuntime && savedRegion != null)
            {
                gotoRegion(savedRegion, savedX, savedY);
            }
        }

        private void zoomTracker_Scroll(object sender, EventArgs e)
        {
            if (mmap != null)
            {
                mmap.Zoom = mmap.MinZoom + (mmap.MaxZoom - mmap.MinZoom) * ((float)zoomTracker.Value / 100f);
            }
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            mmap?.RefreshRegionAgents();
        }

        #endregion GUIEvents

        #region Map friends
        FriendInfo mapFriend = null;
        ulong targetRegionHandle = 0;

        void Friends_FriendFoundReply(object sender, FriendFoundReplyEventArgs e)
        {
            if (mapFriend == null || mapFriend.UUID != e.AgentID) return;

            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => Friends_FriendFoundReply(sender, e)));
                }
                return;
            }

            txtRegion.Text = string.Empty;
            nudX.Value = (int)e.Location.X;
            nudY.Value = (int)e.Location.Y;
            nudZ.Value = (int)e.Location.Z;
            targetRegionHandle = e.RegionHandle;
            uint x, y;
            Utils.LongToUInts(e.RegionHandle, out x, out y);
            x /= 256;
            y /= 256;
            ulong hndle = Utils.UIntsToLong(x, y);
            foreach (KeyValuePair<string, ulong> kvp in regionHandles)
            {
                if (kvp.Value == hndle)
                {
                    txtRegion.Text = kvp.Key;
                    btnTeleport.Enabled = true;
                }
            }
            mmap.CenterMap(x, y, (uint)e.Location.X, (uint)e.Location.Y, true);
        }

        private void ddOnlineFriends_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ddOnlineFriends.SelectedIndex < 1) return;
            mapFriend = client.Friends.FriendList.Find(f => f.Name == ddOnlineFriends.SelectedItem.ToString());
            if (mapFriend != null)
            {
                targetRegionHandle = 0;
                client.Friends.MapFriend(mapFriend.UUID);
            }
        }
        #endregion Map friends
    }
}
