package com.alialyami.widgetscreen.integrations;

import android.service.notification.NotificationListenerService;

// Exists only so the user can grant notification access, which is what lets an app read and control
// other apps' media sessions on Android. It ignores the notifications themselves.
public final class MediaListener extends NotificationListenerService { }
