using System;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;

namespace AutoSweep {
    class PluginUI : IDisposable {
        private readonly Configuration configuration;

        private bool settingsVisible = false;

        public PluginUI(Configuration configuration) {
            this.configuration = configuration;
        }

        public bool SettingsVisible {
            get => settingsVisible;
            set => settingsVisible = value;
        }

        public void Dispose() {
            // we don't need to do anything here
        }

        public void Draw() {
            DrawSettingsWindow();
        }

        private void DrawSettingsWindow() {
            if (!SettingsVisible) return;

            ImGui.SetNextWindowSize(new Vector2(450, 290), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("PaissaHouse Configuration", ref settingsVisible,
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)) {
                // tab bar
                if (ImGui.BeginTabBar("paissatabs")) {
                    // tab: settings
                    if (ImGui.BeginTabItem("Settings")) {
                        // enabled
                        bool enabled = configuration.Enabled;
                        if (ImGui.Checkbox("Enabled", ref enabled)) configuration.Enabled = enabled;
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("停用時不會記錄巡房與抽籤資料，也不會在聊天欄輸出。");

                        // output format
                        OutputFormat outputFormat = configuration.OutputFormat;
                        if (ImGui.BeginCombo("Output Format", outputFormat.ToString())) {
                            foreach (OutputFormat format in Enum.GetValues(typeof(OutputFormat))) {
                                bool selected = format == outputFormat;
                                if (ImGui.Selectable(format.ToString(), selected))
                                    configuration.OutputFormat = format;
                                if (selected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        // custom output format
                        if (configuration.OutputFormat == OutputFormat.Custom) {
                            string customOutputFormat = configuration.OutputFormatString;
                            if (ImGui.InputText("Custom Output Format", ref customOutputFormat, 2000)) configuration.OutputFormatString = customOutputFormat;
                        }

                        // chat type
                        XivChatType outputChatType = configuration.ChatType;
                        if (ImGui.BeginCombo("Output Chat Channel", outputChatType.ToString())) {
                            foreach (XivChatType chatType in Enum.GetValues(typeof(XivChatType))) {
                                bool selected = chatType == outputChatType;
                                if (ImGui.Selectable(chatType.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? chatType.ToString(), selected))
                                    configuration.ChatType = chatType;
                                if (selected)
                                    ImGui.SetItemDefaultFocus();
                            }
                            ImGui.EndCombo();
                        }

                        // sweep chat alert
                        bool chatSweepAlert = configuration.ChatSweepAlert;
                        if (ImGui.Checkbox("Chat Sweep Notifications", ref chatSweepAlert)) configuration.ChatSweepAlert = chatSweepAlert;
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("從以太之光或渡船查看住宅區時，是否在聊天欄顯示巡房開始與空地摘要。");

                        // TW port: auto CSV export
                        bool autoExportCsv = configuration.AutoExportCsv;
                        if (ImGui.Checkbox("巡完整區後自動匯出 CSV", ref autoExportCsv)) configuration.AutoExportCsv = autoExportCsv;
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("巡完一個住宅區的全部區數後，把空地清單寫成 CSV（UTF-8，可用 Excel 開啟）。\n輸入 /phouse 開啟空地清單視窗，可從那裡開啟資料夾。");

                        // TW port: wards per district
                        int wards = configuration.WardsPerDistrict;
                        ImGui.SetNextItemWidth(120);
                        if (ImGui.InputInt("每個住宅區的區數", ref wards)) configuration.WardsPerDistrict = Math.Clamp(wards, 1, 60);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("巡完這麼多區就視為巡完整區（顯示摘要、匯出 CSV）。台服目前為 18，日後開放更多區時再調整。");

                        ImGui.Spacing();
                        ImGui.TextDisabled("台服版：所有資料只存在本機，不會上傳到 PaissaDB。");

                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                    ImGui.Separator();
                }

                // save and close
                if (ImGui.Button("Save and Close")) {
                    configuration.Save();
                    SettingsVisible = false;
                }
            }
            ImGui.End();
        }
    }
}
