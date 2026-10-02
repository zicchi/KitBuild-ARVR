package com.zidane.conceptmapar;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentTransaction;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;

import com.unity3d.player.UnityPlayer;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;

/**
 * File picker via Storage Access Framework (ACTION_OPEN_DOCUMENT).
 * Tidak butuh permission apa pun — sistem yang menampilkan file manager.
 * Hasil: GameObject "JsonPickerCallback" → OnFilePicked(json) / OnFilePickError(msg).
 */
public class JsonFilePicker extends Fragment {

    private static final int  REQUEST_CODE = 48151;
    private static final long MAX_SIZE     = 20_000_000; // 20MB

    public static void pick() {
        Activity activity = UnityPlayer.currentActivity;
        JsonFilePicker fragment = new JsonFilePicker();
        FragmentTransaction ft = activity.getFragmentManager().beginTransaction();
        ft.add(fragment, "json_file_picker");
        ft.commitAllowingStateLoss();
    }

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        try {
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("*/*");
            intent.putExtra(Intent.EXTRA_MIME_TYPES,
                    new String[]{"application/json", "text/plain", "application/octet-stream"});
            startActivityForResult(intent, REQUEST_CODE);
        } catch (Exception e) {
            send("OnFilePickError", "Tidak bisa membuka file manager: " + e.getMessage());
            remove();
        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode != REQUEST_CODE) return;
        try {
            if (resultCode == Activity.RESULT_OK && data != null && data.getData() != null)
                send("OnFilePicked", readUri(data.getData()));
            else
                send("OnFilePickError", "dibatalkan");
        } catch (Exception e) {
            send("OnFilePickError", "Gagal baca file: " + e.getMessage());
        }
        remove();
    }

    private String readUri(Uri uri) throws Exception {
        InputStream is = getActivity().getContentResolver().openInputStream(uri);
        BufferedReader reader = new BufferedReader(new InputStreamReader(is, "UTF-8"));
        StringBuilder sb = new StringBuilder();
        char[] buf = new char[8192];
        int n;
        long total = 0;
        while ((n = reader.read(buf)) > 0) {
            total += n;
            if (total > MAX_SIZE) { reader.close(); throw new Exception("File terlalu besar"); }
            sb.append(buf, 0, n);
        }
        reader.close();
        return sb.toString().trim();
    }

    private void send(String method, String payload) {
        UnityPlayer.UnitySendMessage("JsonPickerCallback", method, payload);
    }

    private void remove() {
        try {
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        } catch (Exception ignored) {}
    }
}
