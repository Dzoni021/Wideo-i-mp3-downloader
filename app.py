"""OmniFetch - a small, accessible media downloader.

The application intentionally delegates site support to yt-dlp.  This means it
can handle public URLs from many supported services without hard-coding site
specific scraping logic.
"""

from __future__ import annotations

import os
import queue
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk
from typing import Any

try:
    import yt_dlp
except ImportError:  # pragma: no cover - gives a friendlier GUI error
    yt_dlp = None  # type: ignore[assignment]


class DownloadCancelled(Exception):
    """Raised internally when the user presses Cancel."""


class OmniFetchApp(tk.Tk):
    """Accessible Tkinter front end for yt-dlp."""

    def __init__(self) -> None:
        super().__init__()
        self.title("OmniFetch – preuzimanje medija")
        self.geometry("720x540")
        self.minsize(620, 470)

        self.events: queue.Queue[tuple[str, Any]] = queue.Queue()
        self.cancel_event = threading.Event()
        self.worker: threading.Thread | None = None

        self.url_var = tk.StringVar()
        self.kind_var = tk.StringVar(value="video")
        self.quality_var = tk.StringVar(value="Najbolji dostupni")
        self.folder_var = tk.StringVar(value=str(Path.home() / "Downloads"))
        self.status_var = tk.StringVar(value="Spremno za preuzimanje.")
        self.progress_var = tk.DoubleVar(value=0)
        self.detail_var = tk.StringVar(value="")

        self._build_ui()
        self.after(100, self._process_events)

    def _build_ui(self) -> None:
        self.columnconfigure(0, weight=1)
        self.rowconfigure(1, weight=1)

        header = ttk.Frame(self, padding=(20, 18, 20, 8))
        header.grid(row=0, column=0, sticky="ew")
        header.columnconfigure(0, weight=1)
        ttk.Label(
            header,
            text="OmniFetch",
            font=("TkDefaultFont", 18, "bold"),
        ).grid(row=0, column=0, sticky="w")
        ttk.Label(
            header,
            text="Jednostavan download javno dostupnog videa i zvuka",
        ).grid(row=1, column=0, sticky="w", pady=(4, 0))

        main = ttk.Frame(self, padding=(20, 8, 20, 16))
        main.grid(row=1, column=0, sticky="nsew")
        main.columnconfigure(0, weight=1)
        main.rowconfigure(5, weight=1)

        url_group = ttk.LabelFrame(main, text="Adresa", padding=12)
        url_group.grid(row=0, column=0, sticky="ew", pady=(0, 10))
        url_group.columnconfigure(0, weight=1)
        url_entry = ttk.Entry(url_group, textvariable=self.url_var)
        url_entry.grid(row=0, column=0, sticky="ew")
        url_entry.focus_set()
        ttk.Label(url_group, text="Nalepite javni URL videa ili stranice.").grid(
            row=1, column=0, sticky="w", pady=(6, 0)
        )

        options = ttk.LabelFrame(main, text="Opcije", padding=12)
        options.grid(row=1, column=0, sticky="ew", pady=(0, 10))
        options.columnconfigure(1, weight=1)
        ttk.Label(options, text="Tip fajla:").grid(row=0, column=0, sticky="w", padx=(0, 12))
        kind_frame = ttk.Frame(options)
        kind_frame.grid(row=0, column=1, sticky="w")
        ttk.Radiobutton(
            kind_frame, text="Video (MP4)", value="video", variable=self.kind_var,
            command=self._update_quality_state,
        ).pack(side="left", padx=(0, 18))
        ttk.Radiobutton(
            kind_frame, text="Audio (MP3)", value="audio", variable=self.kind_var,
            command=self._update_quality_state,
        ).pack(side="left")

        ttk.Label(options, text="Kvalitet:").grid(row=1, column=0, sticky="w", pady=(10, 0))
        self.quality_combo = ttk.Combobox(
            options,
            textvariable=self.quality_var,
            state="readonly",
            values=("Najbolji dostupni", "1080p ili manje", "720p ili manje", "480p ili manje"),
        )
        self.quality_combo.grid(row=1, column=1, sticky="ew", pady=(10, 0))

        folder_group = ttk.LabelFrame(main, text="Odredišni folder", padding=12)
        folder_group.grid(row=2, column=0, sticky="ew", pady=(0, 10))
        folder_group.columnconfigure(0, weight=1)
        ttk.Entry(folder_group, textvariable=self.folder_var).grid(row=0, column=0, sticky="ew")
        ttk.Button(folder_group, text="Izaberi…", command=self._choose_folder).grid(
            row=0, column=1, padx=(10, 0)
        )

        action_frame = ttk.Frame(main)
        action_frame.grid(row=3, column=0, sticky="ew", pady=(2, 10))
        action_frame.columnconfigure(0, weight=1)
        self.download_button = ttk.Button(
            action_frame, text="Preuzmi", command=self._start_download, default="active"
        )
        self.download_button.grid(row=0, column=0, sticky="w")
        self.cancel_button = ttk.Button(
            action_frame, text="Otkaži", command=self._cancel_download, state="disabled"
        )
        self.cancel_button.grid(row=0, column=1, sticky="w", padx=(10, 0))

        progress_group = ttk.LabelFrame(main, text="Napredak", padding=12)
        progress_group.grid(row=4, column=0, sticky="ew", pady=(0, 10))
        progress_group.columnconfigure(0, weight=1)
        ttk.Progressbar(progress_group, variable=self.progress_var, maximum=100).grid(
            row=0, column=0, sticky="ew"
        )
        ttk.Label(progress_group, textvariable=self.detail_var).grid(
            row=1, column=0, sticky="w", pady=(6, 0)
        )

        status = ttk.Label(
            main, textvariable=self.status_var, anchor="w", justify="left", wraplength=680
        )
        status.grid(row=5, column=0, sticky="new")

        ttk.Label(
            self,
            text="Koristite samo sadržaj koji smete da sačuvate. DRM, privatni sadržaj i zaobilaženje zaštita nisu podržani.",
            padding=(20, 0, 20, 14),
            wraplength=720,
        ).grid(row=2, column=0, sticky="w")

        self._update_quality_state()
        self.bind("<Return>", lambda _event: self._start_download())

    def _update_quality_state(self) -> None:
        if self.kind_var.get() == "audio":
            self.quality_combo.configure(values=("MP3 320 kbps", "MP3 192 kbps", "MP3 128 kbps"))
            self.quality_var.set("MP3 192 kbps")
        else:
            self.quality_combo.configure(
                values=("Najbolji dostupni", "1080p ili manje", "720p ili manje", "480p ili manje")
            )
            self.quality_var.set("Najbolji dostupni")

    def _choose_folder(self) -> None:
        selected = filedialog.askdirectory(initialdir=self.folder_var.get())
        if selected:
            self.folder_var.set(selected)

    def _start_download(self) -> None:
        if self.worker and self.worker.is_alive():
            return
        if yt_dlp is None:
            messagebox.showerror(
                "Nedostaje zavisnost",
                "Instalirajte zavisnosti komandom:\n\npython -m pip install -r requirements.txt",
            )
            return

        url = self.url_var.get().strip()
        folder = self.folder_var.get().strip()
        if not url:
            messagebox.showwarning("Nedostaje URL", "Unesite URL koji želite da preuzmete.")
            return
        if not folder:
            messagebox.showwarning("Nedostaje folder", "Izaberite odredišni folder.")
            return

        Path(folder).mkdir(parents=True, exist_ok=True)
        self.cancel_event.clear()
        self.progress_var.set(0)
        self.detail_var.set("")
        self.status_var.set("Preuzimanje je u toku…")
        self.download_button.configure(state="disabled")
        self.cancel_button.configure(state="normal")
        self.worker = threading.Thread(target=self._download, args=(url, folder), daemon=True)
        self.worker.start()

    def _cancel_download(self) -> None:
        if self.worker and self.worker.is_alive():
            self.cancel_event.set()
            self.status_var.set("Otkazivanje…")
            self.cancel_button.configure(state="disabled")

    def _download(self, url: str, folder: str) -> None:
        assert yt_dlp is not None

        def progress_hook(data: dict[str, Any]) -> None:
            if self.cancel_event.is_set():
                raise DownloadCancelled()
            state = data.get("status")
            if state == "downloading":
                percent = self._percent(data)
                speed = data.get("speed")
                eta = data.get("eta")
                detail = f"{percent:.1f}%"
                if speed:
                    detail += f" • {self._format_bytes(speed)}/s"
                if eta is not None:
                    detail += f" • preostalo {eta}s"
                self.events.put(("progress", (percent, detail)))
            elif state == "finished":
                self.events.put(("progress", (100.0, "Obrada fajla…")))

        is_audio = self.kind_var.get() == "audio"
        quality = self.quality_var.get()
        if is_audio:
            bitrate = {"MP3 320 kbps": "320", "MP3 128 kbps": "128"}.get(quality, "192")
            format_selector = "bestaudio/best"
            postprocessors = [{"key": "FFmpegExtractAudio", "preferredcodec": "mp3", "preferredquality": bitrate}]
        else:
            limits = {"1080p ili manje": 1080, "720p ili manje": 720, "480p ili manje": 480}
            height = limits.get(quality)
            format_selector = (
                f"bestvideo[height<={height}]+bestaudio/best[height<={height}]/best"
                if height
                else "bestvideo+bestaudio/best"
            )
            postprocessors = []

        options: dict[str, Any] = {
            "outtmpl": os.path.join(folder, "%(title)s.%(ext)s"),
            "format": format_selector,
            "noplaylist": True,
            "progress_hooks": [progress_hook],
            "quiet": True,
            "no_warnings": True,
            "windowsfilenames": True,
            "postprocessors": postprocessors,
            "merge_output_format": "mp4" if not is_audio else None,
        }
        try:
            with yt_dlp.YoutubeDL(options) as downloader:
                downloader.download([url])
            self.events.put(("done", "Preuzimanje je završeno."))
        except DownloadCancelled:
            self.events.put(("cancelled", "Preuzimanje je otkazano."))
        except Exception as error:  # yt-dlp exposes useful human-readable errors
            self.events.put(("error", str(error)))

    @staticmethod
    def _percent(data: dict[str, Any]) -> float:
        value = data.get("_percent_str", "0%").strip().replace("%", "")
        try:
            return float(value)
        except ValueError:
            downloaded = data.get("downloaded_bytes", 0)
            total = data.get("total_bytes") or data.get("total_bytes_estimate") or 0
            return downloaded / total * 100 if total else 0

    @staticmethod
    def _format_bytes(value: float) -> str:
        units = ("B", "KB", "MB", "GB")
        size = float(value)
        for unit in units:
            if size < 1024 or unit == units[-1]:
                return f"{size:.1f} {unit}"
            size /= 1024
        return f"{size:.1f} GB"

    def _process_events(self) -> None:
        try:
            while True:
                event, value = self.events.get_nowait()
                if event == "progress":
                    percent, detail = value
                    self.progress_var.set(percent)
                    self.detail_var.set(detail)
                elif event == "done":
                    self._finish(value)
                    messagebox.showinfo("Završeno", value)
                elif event == "cancelled":
                    self._finish(value)
                elif event == "error":
                    self._finish("Greška pri preuzimanju.")
                    messagebox.showerror("Preuzimanje nije uspelo", value)
        except queue.Empty:
            pass
        self.after(100, self._process_events)

    def _finish(self, message: str) -> None:
        self.status_var.set(message)
        self.download_button.configure(state="normal")
        self.cancel_button.configure(state="disabled")


if __name__ == "__main__":
    app = OmniFetchApp()
    app.mainloop()
