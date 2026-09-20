package com.alphadelta.electroniclogbook;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.widget.Toast;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

public class ElectronicLogbookExportDownloadActivity extends Activity {
    private static final int CreateDocumentRequest = 1;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (savedInstanceState != null) {
            return;
        }

        String fileName = getIntent().getStringExtra(Intent.EXTRA_TITLE);
        Uri source = getIntent().getData();
        if (!ElectronicLogbookNativeFilesPlugin.isSupportedExportFileName(fileName) || source == null) {
            Toast.makeText(this, "Could not prepare the exported file.", Toast.LENGTH_LONG).show();
            finish();
            return;
        }

        Intent saveIntent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        saveIntent.addCategory(Intent.CATEGORY_OPENABLE);
        saveIntent.setType(getIntent().getType());
        saveIntent.putExtra(Intent.EXTRA_TITLE, fileName);
        startActivityForResult(saveIntent, CreateDocumentRequest);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != CreateDocumentRequest) {
            return;
        }

        Uri destination = data == null ? null : data.getData();
        if (resultCode == RESULT_OK && destination != null) {
            copyExport(destination);
        }
        finish();
    }

    private void copyExport(Uri destination) {
        try (InputStream input = getContentResolver().openInputStream(getIntent().getData());
             OutputStream output = getContentResolver().openOutputStream(destination, "w")) {
            if (input == null || output == null) {
                throw new IOException("Android did not provide a readable export and writable destination.");
            }
            byte[] buffer = new byte[8192];
            int read;
            while ((read = input.read(buffer)) != -1) {
                output.write(buffer, 0, read);
            }
            Toast.makeText(this, "Downloaded " + getIntent().getStringExtra(Intent.EXTRA_TITLE) + ".", Toast.LENGTH_LONG).show();
        } catch (IOException exception) {
            getContentResolver().delete(destination, null, null);
            Toast.makeText(this, "Could not download the exported file.", Toast.LENGTH_LONG).show();
        }
    }
}
