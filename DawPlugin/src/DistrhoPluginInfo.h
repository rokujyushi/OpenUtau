#ifndef DISTRHO_PLUGIN_INFO_H_INCLUDED
#define DISTRHO_PLUGIN_INFO_H_INCLUDED

#define DISTRHO_PLUGIN_BRAND "stakira"
#ifdef DEBUG
#define DISTRHO_PLUGIN_NAME "OpenUtau Bridge (Debug)"
#else
#define DISTRHO_PLUGIN_NAME "OpenUtau Bridge"
#endif
#define DISTRHO_PLUGIN_URI "https://github.com/stakira/OpenUtau/"

#define DISTRHO_PLUGIN_BRAND_ID Stak
#ifdef DEBUG
#define DISTRHO_PLUGIN_UNIQUE_ID OpUD
#define DISTRHO_PLUGIN_CLAP_ID "stakira.openutau-bridge-debug"
#else
#define DISTRHO_PLUGIN_UNIQUE_ID OpUt
#define DISTRHO_PLUGIN_CLAP_ID "stakira.openutau-bridge"
#endif

#define DISTRHO_PLUGIN_HAS_UI 1
#define DISTRHO_PLUGIN_IS_SYNTH 1
#define DISTRHO_PLUGIN_IS_RT_SAFE 1
#define DISTRHO_PLUGIN_NUM_INPUTS 0
#define DISTRHO_PLUGIN_NUM_OUTPUTS 32
#define DISTRHO_PLUGIN_WANT_TIMEPOS 1
#define DISTRHO_PLUGIN_WANT_STATE 1
#define DISTRHO_PLUGIN_WANT_FULL_STATE 1
#define DISTRHO_UI_DEFAULT_WIDTH 1024
#define DISTRHO_UI_DEFAULT_HEIGHT 256
#define DISTRHO_UI_FILE_BROWSER 1
#define DISTRHO_UI_USER_RESIZABLE 1
#define DISTRHO_PLUGIN_WANT_DIRECT_ACCESS 1

#define DISTRHO_UI_USE_CUSTOM 1
#define DISTRHO_UI_CUSTOM_INCLUDE_PATH "dpf_widgets/opengl/DearImGui.hpp"
#define DISTRHO_UI_CUSTOM_WIDGET_TYPE DGL_NAMESPACE::ImGuiTopLevelWidget

#endif // DISTRHO_PLUGIN_INFO_H_INCLUDED
