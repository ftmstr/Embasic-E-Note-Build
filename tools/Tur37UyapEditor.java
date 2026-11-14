import java.awt.Component;
import java.awt.Container;
import java.awt.Frame;
import java.awt.Robot;
import java.awt.Window;
import java.awt.event.KeyEvent;
import java.io.File;
import javax.swing.JDialog;
import javax.swing.SwingUtilities;
import javax.swing.text.JTextComponent;

/** Drives the editor's visible Swing document and its normal Save keyboard shortcut. */
public final class Tur37UyapEditor {
    private static final String Heading = "EMBASIC E-NOTE TUR37";
    private static final String Marker = " TUR37-EDIT";

    public static void main(String[] args) throws Exception {
        try {
            run(args);
            System.exit(0);
        } catch (Throwable error) {
            error.printStackTrace();
            System.exit(1);
        }
    }

    private static void run(String[] args) throws Exception {
        boolean edit = args[0].equals("edit");
        File input = new File(args[1]);
        long previousSize = input.length();
        long previousTime = input.lastModified();
        Thread launcher = new Thread(() -> {
            try {
                Class<?> manager = Class.forName("tr.com.havelsan.uyap.system.editor.common.WPAppManager");
                manager.getMethod("main", String[].class).invoke(null, (Object) new String[] {
                    "getNewWPInstance", "EDITOR_TYPE_DOCUMENT", input.getAbsolutePath()
                });
            } catch (Exception error) {
                throw new IllegalStateException(error);
            }
        });
        launcher.setUncaughtExceptionHandler((thread, error) -> { error.printStackTrace(); System.exit(1); });
        launcher.start();

        JTextComponent[] document = new JTextComponent[1];
        Frame[] frame = new Frame[1];
        long deadline = System.nanoTime() + 45_000_000_000L;
        while (document[0] == null && System.nanoTime() < deadline) {
            SwingUtilities.invokeAndWait(() -> {
                for (Window window : Window.getWindows()) {
                    if (window instanceof JDialog && window.isShowing() &&
                        ((JDialog) window).getTitle().contains("Son Sürümlerde Yapılan Güncellemeler")) {
                        window.dispose();
                    }
                }
                for (Frame candidate : Frame.getFrames()) {
                    if (!candidate.isVisible() || !candidate.getTitle().contains(input.getName()) ||
                        !candidate.getTitle().contains("5.4.20")) continue;
                    JTextComponent component = findDocument(candidate);
                    if (component != null) { document[0] = component; frame[0] = candidate; return; }
                }
            });
            if (document[0] == null) Thread.sleep(250);
        }
        if (document[0] == null) throw new IllegalStateException("Visible UYAP 5.4.20 document was not found.");
        SwingUtilities.invokeAndWait(() -> {
            JTextComponent pane = document[0];
            String text = documentText(pane);
            if (!text.contains("\u00c7\u0131\u011f ve \u0130\u011fd\u0131r") ||
                !text.contains("SON PARAGRAF TUR37") || (!edit && !text.contains(Marker)))
                throw new IllegalStateException("The editor text differs from the expected fixture.");
            if (edit) {
                int position = text.indexOf(Heading) + Heading.length();
                pane.setCaretPosition(position);
                pane.replaceSelection(Marker);
                if (!documentText(pane).contains(Heading + Marker))
                    throw new IllegalStateException("The visible editor rejected the change.");
                frame[0].toFront();
                pane.requestFocusInWindow();
            }
        });

        if (edit) {
            Robot keyboard = new Robot();
            keyboard.waitForIdle();
            keyboard.keyPress(KeyEvent.VK_CONTROL);
            keyboard.keyPress(KeyEvent.VK_S);
            keyboard.keyRelease(KeyEvent.VK_S);
            keyboard.keyRelease(KeyEvent.VK_CONTROL);
            deadline = System.nanoTime() + 20_000_000_000L;
            while (input.length() == previousSize && input.lastModified() == previousTime &&
                   System.nanoTime() < deadline) Thread.sleep(200);
            if (input.length() == previousSize && input.lastModified() == previousTime)
                throw new IllegalStateException("The editor did not save the clone.");
            keyboard.waitForIdle();
        }
        System.out.println(edit ? "UYAP_EDIT_AND_SAVE_OK" : "UYAP_REOPEN_OK");
    }

    private static JTextComponent findDocument(Component component) {
        if (component instanceof JTextComponent && component.isShowing()) {
            JTextComponent text = (JTextComponent) component;
            if (text.isEditable() && documentText(text).contains(Heading)) return text;
        }
        if (component instanceof Container) {
            for (Component child : ((Container) component).getComponents()) {
                JTextComponent match = findDocument(child);
                if (match != null) return match;
            }
        }
        return null;
    }

    private static String documentText(JTextComponent text) {
        try { return text.getDocument().getText(0, text.getDocument().getLength()); }
        catch (javax.swing.text.BadLocationException error) { throw new IllegalStateException(error); }
    }
}
