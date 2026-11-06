"""Tie distributable packages to a clean Git commit and its actual DLL."""
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def package_identity(plugin):
    if git("status", "--porcelain", "--untracked-files=normal"):
        raise SystemExit("Commit all source changes before packaging. Dirty builds are for local testing only.")
    commit = git("rev-parse", "HEAD")
    marker = "git." + commit
    data = Path(plugin).read_bytes()
    # AssemblyInformationalVersion is a UTF-8 serialized custom attribute.
    # Require its terminator too: a .dirty DLL must never pass as a clean one.
    if (marker.encode("ascii") + b"\x00") not in data:
        raise SystemExit("DLL was not built from this clean commit. Rebuild both targets after committing.")
    return {"commit": commit, "shortCommit": commit[:12], "buildId": "g" + commit[:12],
            "informationalVersion": marker, "dirty": False}


def build_note(identity, version):
    return ("AssetImport build: " + identity["buildId"] + "\n"
            "Commit: " + identity["commit"] + "\n"
            "Plugin version: " + version + "\n"
            "Report this build ID and BepInEx/LogOutput.log (or output_log.txt).\n").replace("\n", "\r\n").encode("utf-8-sig")


def source_archive_files():
    # Export Git's committed tree, never ignored private models, SDKs or game DLLs.
    names = subprocess.check_output(["git", "ls-tree", "-rz", "--name-only", "HEAD"], cwd=ROOT)
    for raw in names.split(b"\0"):
        if raw:
            name = raw.decode("utf-8")
            data = subprocess.check_output(["git", "show", "HEAD:" + name], cwd=ROOT)
            yield name, data
