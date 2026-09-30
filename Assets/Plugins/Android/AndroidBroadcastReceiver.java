package com.quest.debug;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import com.unity3d.player.UnityPlayer;

public class AndroidBroadcastReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent != null) {
            StringBuilder sb = new StringBuilder();
            sb.append("[").append(intent.getAction()).append("]\n");

            Bundle bundle = intent.getExtras();
            if (bundle != null && !bundle.isEmpty()) {
                for (String key : bundle.keySet()) {
                    Object value = bundle.get(key);
                    sb.append("  ↳ ").append(key).append(" : ").append(value).append("\n");
                }
            } else {
                sb.append("  ↳ (No data)\n");
            }
            sb.append("--------------------\n");

            // 发送给 C# 层
            UnityPlayer.UnitySendMessage("AndroidDebugger", "OnBroadcastReceived", sb.toString());
        }
    }
}