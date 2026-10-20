package com.alialyami.widgetscreen.integrations;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

// Android's counterpart of DPAPI: text is encrypted with an AES key that lives in the Android Keystore
// and never leaves this device, then written atomically to the app's private folder.
public final class SecureStore {
    private static final String ALIAS = "widget-screen-session";

    private static synchronized SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(ALIAS)) return ((KeyStore.SecretKeyEntry) store.getEntry(ALIAS, null)).getSecretKey();
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build());
        return generator.generateKey();
    }

    public static void write(File file, String text) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        byte[] iv = cipher.getIV(), data = cipher.doFinal(text.getBytes(StandardCharsets.UTF_8));
        AtomicFile atomic = new AtomicFile(file);
        FileOutputStream out = atomic.startWrite();
        try {
            out.write(iv.length); out.write(iv); out.write(data);
            atomic.finishWrite(out);
        } catch (IOException e) { atomic.failWrite(out); throw e; }
    }

    // Null when nothing is stored.
    public static String read(File file) throws Exception {
        AtomicFile atomic = new AtomicFile(file);
        if (!file.exists()) return null;
        byte[] all = atomic.readFully();
        if (all.length < 2 || all[0] <= 0 || all[0] + 1 >= all.length) throw new IOException("The stored session is damaged.");
        int ivLength = all[0];
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, Arrays.copyOfRange(all, 1, 1 + ivLength)));
        return new String(cipher.doFinal(all, 1 + ivLength, all.length - 1 - ivLength), StandardCharsets.UTF_8);
    }
}
