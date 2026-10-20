package com.alialyami.widgetscreen.integrations;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.util.LruCache;

// Cover art for list rows and the now-playing strip: decoded at the size shown, kept in a small
// in-memory cache, never written to disk.
public final class Images {
    public interface Loaded { void loaded(Bitmap bitmap); }

    private final LruCache<String, Bitmap> cache = new LruCache<String, Bitmap>(120);

    public Bitmap cached(String url) { return url == null ? null : cache.get(url); }

    public void load(final String url, final int sizePx, final Loaded done) {
        Bitmap hit = cached(url);
        if (hit != null) { done.loaded(hit); return; }
        Async.run(new Async.Work<Bitmap>() {
            @Override public Bitmap run() throws Exception { return decode(Http.getBytes(url, 2 * 1024 * 1024), sizePx); }
        }, new Async.Done<Bitmap>() {
            @Override public void ok(Bitmap bitmap) { if (bitmap != null) { cache.put(url, bitmap); done.loaded(bitmap); } }
        }, null);
    }

    public static Bitmap decode(byte[] data, int sizePx) {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        BitmapFactory.decodeByteArray(data, 0, data.length, bounds);
        int sample = 1;
        while (bounds.outWidth / (sample * 2) >= sizePx && bounds.outHeight / (sample * 2) >= sizePx) sample *= 2;
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = sample;
        return BitmapFactory.decodeByteArray(data, 0, data.length, options);
    }

    public static Bitmap shrink(Bitmap bitmap, int sizePx) {
        if (bitmap == null || (bitmap.getWidth() <= sizePx && bitmap.getHeight() <= sizePx)) return bitmap;
        float scale = sizePx / (float) Math.max(bitmap.getWidth(), bitmap.getHeight());
        return Bitmap.createScaledBitmap(bitmap, Math.max(1, Math.round(bitmap.getWidth() * scale)), Math.max(1, Math.round(bitmap.getHeight() * scale)), true);
    }
}
