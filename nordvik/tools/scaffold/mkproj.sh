#!/bin/bash
# Uso: mkproj.sh <carpeta del servicio> <NombreEnsamblado>
mkdir -p "$1"
depth=$(echo "$1" | awk -F/ '{print NF}')
up=$(printf '../%.0s' $(seq 1 $depth))
cat > "$1/$2.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Nordvik.Services</RootNamespace>
    <AssemblyName>Nordvik.Svc.$2</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="${up}platform/Nordvik.Shared/Nordvik.Shared.csproj" />
  </ItemGroup>
</Project>
XML
