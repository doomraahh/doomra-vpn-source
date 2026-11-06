namespace Luna {
 internal static class UiState {
  internal static bool IsOnline(string state,bool active,bool cancelled,bool closing) { return state=="CONNECTED" && active && !cancelled && !closing; }
 }
}
