﻿/**
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
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class RegionInfo : RadegastTabControl
    {
        Timer refresh;
        UUID parcelGroupID = UUID.Zero;

        public RegionInfo()
            : this(null)
        {
        }

        public RegionInfo(RadegastInstance instance)
            : base(instance)
        {
            InitializeComponent();
            Disposed += RegionInfo_Disposed;

            refresh = new Timer()
            {
                Enabled = false,
                Interval = 1000,
            };

            client.Groups.GroupNamesReply += Groups_GroupNamesReply;
            client.Parcels.ParcelProperties += Parcels_ParcelProperties;
            client.Parcels.ParcelDwellReply += Parcels_ParcelDwellReply;
            refresh.Tick += refresh_Tick;
            refresh.Enabled = true;
            UpdateDisplay();
            client.Parcels.RequestDwell(client.Network.CurrentSim, instance.State.Parcel.LocalID);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        void RegionInfo_Disposed(object sender, EventArgs e)
        {
            client.Groups.GroupNamesReply -= Groups_GroupNamesReply;
            client.Parcels.ParcelProperties -= Parcels_ParcelProperties;
            client.Parcels.ParcelDwellReply -= Parcels_ParcelDwellReply;
            refresh.Enabled = false;
            refresh.Dispose();
            refresh = null;
        }

        void Parcels_ParcelProperties(object sender, ParcelPropertiesEventArgs e)
        {
            if (instance.MainForm.PreventParcelUpdate || e.Result != ParcelResult.Single) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => Parcels_ParcelProperties(sender, e)));
                return;
            }

            UpdateParcelDisplay();
        }

        void Parcels_ParcelDwellReply(object sender, ParcelDwellReplyEventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => Parcels_ParcelDwellReply(sender, e)));
                return;
            }

            lblTraffic.Text = e.Dwell.ToString("0");
        }

        void Groups_GroupNamesReply(object sender, GroupNamesEventArgs e)
        {
            if (!e.GroupNames.ContainsKey(parcelGroupID)) return;

            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                {
                    BeginInvoke(new MethodInvoker(() => Groups_GroupNamesReply(sender, e)));
                }
                return;
            }

            lblGroup.Text = e.GroupNames[parcelGroupID];
        }

        void refresh_Tick(object sender, EventArgs e)
        {
            UpdateSimDisplay();
        }

        public void UpdateDisplay()
        {
            UpdateSimDisplay();
            UpdateParcelDisplay();
        }

        void UpdateSimDisplay()
        {
            if (!client.Network.Connected) return;
            if (!Visible) return;

            var s = client.Network.CurrentSim.Stats;

            lblRegionName.Text = client.Network.CurrentSim.Name;
            lblDilation.Text = string.Format("{0:0.000}", s.Dilation);
            lblFPS.Text = s.FPS.ToString();
            lblMainAgents.Text = s.Agents.ToString();
            lblChildAgents.Text = s.ChildAgents.ToString();
            lblObjects.Text = s.Objects.ToString();
            lblActiveObjects.Text = s.ScriptedObjects.ToString();
            lblActiveScripts.Text = s.ActiveScripts.ToString();
            lblPendingDownloads.Text = s.PendingDownloads.ToString();
            lblPendingUploads.Text = (s.PendingLocalUploads + s.PendingUploads).ToString();

            float total = s.NetTime + s.PhysicsTime + s.OtherTime + s.AgentTime + s.AgentTime +
                s.ImageTime + s.ImageTime + s.ScriptTime;
            lblTotalTime.Text = string.Format("{0:0.0} ms", s.FrameTime);
            lblNetTime.Text = string.Format("{0:0.0} ms", s.NetTime);
            lblPhysicsTime.Text = string.Format("{0:0.0} ms", s.PhysicsTime);
            lblSimTime.Text = string.Format("{0:0.0} ms", s.OtherTime);
            lblAgentTime.Text = string.Format("{0:0.0} ms", s.AgentTime);
            lblImagesTime.Text = string.Format("{0:0.0} ms", s.ImageTime);
            lblScriptTime.Text = string.Format("{0:0.0} ms", s.ScriptTime);
            lblSpareTime.Text = string.Format("{0:0.0} ms", Math.Max(0f, 1000f / 45f - total));

            lblCPUClass.Text = client.Network.CurrentSim.CPUClass.ToString();
            lblDataCenter.Text = client.Network.CurrentSim.ColoLocation;
            lblVersion.Text = client.Network.CurrentSim.SimVersion;
        }

        void UpdateParcelDisplay()
        {
            Parcel p = instance.State.Parcel;
            txtParcelTitle.Text = p.Name;
            txtParcelDescription.Text = p.Desc;
            lblSimType.Text = client.Network.CurrentSim.ProductName;

            pnlParcelImage.Controls.Clear();

            if (p.SnapshotID != UUID.Zero)
            {
                SLImageHandler imgParcel = new SLImageHandler {Dock = DockStyle.Fill};
                pnlParcelImage.Controls.Add(imgParcel);
                imgParcel.Init(instance, p.SnapshotID, string.Empty);
            }

            if (p.IsGroupOwned)
            {
                txtOwner.Text = "(Group owned)";
            }
            else
            {
                txtOwner.AgentID = p.OwnerID;
            }

            if (p.GroupID != UUID.Zero)
            {
                parcelGroupID = p.GroupID;
                client.Groups.RequestGroupName(p.GroupID);
            }

            lblTraffic.Text = string.Format("{0:0}", p.Dwell);
            lblSimPrims.Text = string.Format("{0} / {1}", p.SimWideTotalPrims, p.SimWideMaxPrims);
            lblParcelPrims.Text = string.Format("{0} / {1}", p.TotalPrims, p.MaxPrims);
            lblAutoReturn.Text = p.OtherCleanTime.ToString();
            lblArea.Text = p.Area.ToString();
        }

        private void btnRestart_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(new WindowWrapper(Handle),
                "Do you want to restart region " + client.Network.CurrentSim.Name + "?",
                "Confirm restart", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                client.Estate.RestartRegion();
            }
        }
    }
}
