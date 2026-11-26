# -*- mode: ruby -*-
# vi: set ft=ruby :
#
# CocinaPatito / SplitBill — development environment.
#
# Uses Docker as the Vagrant provider: instead of a full virtual machine, Vagrant builds and runs
# ONE Ubuntu 24.04 container (provision/Dockerfile.dev) and provisions it with provision/bootstrap.sh,
# which installs the .NET 10 SDK, PostgreSQL 18 and the project's NuGet packages, and creates the
# database.
#
# Before the first `vagrant up`:
#   1. Copy provision/db.env.example to provision/db.env (ignored by git).
#   2. Edit the database user and passwords in provision/db.env.
#
# Usage (from this folder, with Docker Desktop running):
#   vagrant up --provider=docker     create the container and run bootstrap.sh (first time)
#   vagrant ssh                      open a shell inside the container (project is at /vagrant)
#   vagrant halt                     stop the container
#   vagrant up                       start it again (PostgreSQL is started automatically)
#   vagrant provision                re-run bootstrap.sh (idempotent)
#   vagrant destroy -f               delete the container (the database inside is lost)

Vagrant.configure("2") do |config|
  # Docker is the only provider this environment supports.
  ENV["VAGRANT_DEFAULT_PROVIDER"] = "docker"

  config.vm.provider "docker" do |docker|
    docker.name            = "cocinapatito-dev"
    docker.build_dir       = "provision"
    docker.dockerfile      = "Dockerfile.dev"
    docker.has_ssh         = true   # Vagrant provisions and `vagrant ssh` through sshd in the container
    docker.remains_running = true
    docker.create_args     = ["--hostname", "cocinapatito-dev"]

    # Published ports are bound to 127.0.0.1 only: nothing is reachable from other machines.
    # PostgreSQL uses 5433 on Windows because a local PostgreSQL service usually owns 5432.
    docker.ports = [
      "127.0.0.1:5433:5432",   # PostgreSQL 18 (connect from Windows to 127.0.0.1:5433)
      "127.0.0.1:8080:8080",   # SplitBill API (next implementation block)
    ]
  end

  # Development-only login for the container's sshd. Vagrant replaces it with a generated key pair
  # on first boot (insert_key). Port 22 is published on 127.0.0.1 only.
  config.ssh.username   = "vagrant"
  config.ssh.password   = "vagrant"
  config.ssh.insert_key = true

  # The repository is mounted inside the container at /vagrant.
  config.vm.synced_folder ".", "/vagrant"

  # One-time setup: .NET SDK, PostgreSQL, database, NuGet restore and build. Safe to re-run.
  config.vm.provision "bootstrap", type: "shell", path: "provision/bootstrap.sh", privileged: true

  # Containers have no systemd: start PostgreSQL every time the container comes up.
  config.vm.provision "start-postgres", type: "shell", run: "always", privileged: true, inline: <<-SHELL
    if command -v pg_ctlcluster >/dev/null 2>&1; then
      pg_ctlcluster 18 main start 2>/dev/null || true
      pg_lsclusters
    fi
  SHELL
end
