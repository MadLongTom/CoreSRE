import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import type {
  AgentCard,
  AgentSkill,
  AgentInterface,
  SecurityScheme,
} from "@/types/agent";

interface AgentCardSectionProps {
  card: AgentCard;
  editing?: boolean;
  onChange?: (card: AgentCard) => void;
}

export default function AgentCardSection({
  card,
  editing = false,
  onChange,
}: AgentCardSectionProps) {
  // ---------- helpers for editing ----------
  const updateSkills = (skills: AgentSkill[]) =>
    onChange?.({ ...card, skills });
  const updateInterfaces = (interfaces: AgentInterface[]) =>
    onChange?.({ ...card, interfaces });
  const updateSchemes = (securitySchemes: SecurityScheme[]) =>
    onChange?.({ ...card, securitySchemes });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Agent Card</CardTitle>
      </CardHeader>
      <CardContent className="space-y-6">
        {/* ---- Skills ---- */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <Label className="text-base font-semibold">Skills</Label>
            {editing && (
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  updateSkills([...card.skills, { name: "", description: "" }])
                }
              >
                <Plus className="mr-1 h-3 w-3" />
                添加
              </Button>
            )}
          </div>

          {card.skills.length === 0 && (
            <p className="text-sm text-muted-foreground">无 Skills</p>
          )}

          {editing
            ? card.skills.map((skill, i) => (
                <div key={i} className="flex gap-2">
                  <Input
                    placeholder="名称"
                    value={skill.name}
                    onChange={(e) => {
                      const updated = [...card.skills];
                      updated[i] = { ...updated[i], name: e.target.value };
                      updateSkills(updated);
                    }}
                  />
                  <Input
                    placeholder="描述"
                    value={skill.description ?? ""}
                    onChange={(e) => {
                      const updated = [...card.skills];
                      updated[i] = {
                        ...updated[i],
                        description: e.target.value,
                      };
                      updateSkills(updated);
                    }}
                  />
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() =>
                      updateSkills(card.skills.filter((_, j) => j !== i))
                    }
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </div>
              ))
            : card.skills.map((skill, i) => (
                <div key={i} className="text-sm">
                  <span className="font-medium">{skill.name}</span>
                  {skill.description && (
                    <span className="text-muted-foreground ml-2">
                      — {skill.description}
                    </span>
                  )}
                </div>
              ))}
        </div>

        <Separator />

        {/* ---- Interfaces ---- */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <Label className="text-base font-semibold">Interfaces</Label>
            {editing && (
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  updateInterfaces([
                    ...card.interfaces,
                    { protocol: "", path: "" },
                  ])
                }
              >
                <Plus className="mr-1 h-3 w-3" />
                添加
              </Button>
            )}
          </div>

          {card.interfaces.length === 0 && (
            <p className="text-sm text-muted-foreground">无 Interfaces</p>
          )}

          {editing
            ? card.interfaces.map((iface, i) => (
                <div key={i} className="flex gap-2">
                  <Input
                    placeholder="Protocol"
                    value={iface.protocol}
                    onChange={(e) => {
                      const updated = [...card.interfaces];
                      updated[i] = {
                        ...updated[i],
                        protocol: e.target.value,
                      };
                      updateInterfaces(updated);
                    }}
                  />
                  <Input
                    placeholder="Path"
                    value={iface.path ?? ""}
                    onChange={(e) => {
                      const updated = [...card.interfaces];
                      updated[i] = { ...updated[i], path: e.target.value };
                      updateInterfaces(updated);
                    }}
                  />
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() =>
                      updateInterfaces(
                        card.interfaces.filter((_, j) => j !== i),
                      )
                    }
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </div>
              ))
            : card.interfaces.map((iface, i) => (
                <div key={i} className="text-sm">
                  <span className="font-medium">{iface.protocol}</span>
                  {iface.path && (
                    <span className="text-muted-foreground ml-2">
                      {iface.path}
                    </span>
                  )}
                </div>
              ))}
        </div>

        <Separator />

        {/* ---- Security Schemes ---- */}
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <Label className="text-base font-semibold">Security Schemes</Label>
            {editing && (
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  updateSchemes([
                    ...card.securitySchemes,
                    { type: "", parameters: "" },
                  ])
                }
              >
                <Plus className="mr-1 h-3 w-3" />
                添加
              </Button>
            )}
          </div>

          {card.securitySchemes.length === 0 && (
            <p className="text-sm text-muted-foreground">
              无 Security Schemes
            </p>
          )}

          {editing
            ? card.securitySchemes.map((scheme, i) => (
                <div key={i} className="flex gap-2">
                  <Input
                    placeholder="Type"
                    value={scheme.type}
                    onChange={(e) => {
                      const updated = [...card.securitySchemes];
                      updated[i] = { ...updated[i], type: e.target.value };
                      updateSchemes(updated);
                    }}
                  />
                  <Input
                    placeholder="Parameters"
                    value={scheme.parameters ?? ""}
                    onChange={(e) => {
                      const updated = [...card.securitySchemes];
                      updated[i] = {
                        ...updated[i],
                        parameters: e.target.value,
                      };
                      updateSchemes(updated);
                    }}
                  />
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() =>
                      updateSchemes(
                        card.securitySchemes.filter((_, j) => j !== i),
                      )
                    }
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </div>
              ))
            : card.securitySchemes.map((scheme, i) => (
                <div key={i} className="text-sm">
                  <span className="font-medium">{scheme.type}</span>
                  {scheme.parameters && (
                    <span className="text-muted-foreground ml-2">
                      {scheme.parameters}
                    </span>
                  )}
                </div>
              ))}
        </div>
      </CardContent>
    </Card>
  );
}
